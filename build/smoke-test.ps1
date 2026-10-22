[CmdletBinding()]
param([string]$ZipPath = '', [string]$TestFolder = '')
$ErrorActionPreference = 'Stop'
if (!$ZipPath) { $ZipPath = Join-Path $PSScriptRoot '../artifacts/Atoll-Windows-x64.zip' }
if ($TestFolder) { $testRoot = (Resolve-Path -LiteralPath $TestFolder).Path }
else {
    $testRoot = Join-Path ([IO.Path]::GetTempPath()) ('Atoll-smoke-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Force $testRoot | Out-Null
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::ExtractToDirectory([IO.Path]::GetFullPath($ZipPath), $testRoot)
}
$exe = Join-Path $testRoot 'Atoll/Atoll.exe'
if (!(Test-Path -LiteralPath $exe)) { throw 'Atoll.exe missing from extracted ZIP' }
$ollamaProcesses = @(Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.ProcessName -in @('ollama', 'ollama app') })
$userApp = $ollamaProcesses | Where-Object ProcessName -eq 'ollama app' | Select-Object -First 1
$server = $ollamaProcesses | Where-Object ProcessName -eq 'ollama' | Select-Object -First 1
$appPath = if ($userApp) { $userApp.Path } else { $null }
$serverPath = if ($server) { $server.Path } else { $null }
try {
    # Stop the tray app first so it cannot respawn the server during the test.
    $ollamaProcesses | Where-Object ProcessName -eq 'ollama app' | Stop-Process -Force
    foreach ($p in ($ollamaProcesses | Where-Object ProcessName -eq 'ollama app')) { $p.WaitForExit(5000) | Out-Null }
    $servers = @(Get-Process -Name 'ollama' -ErrorAction SilentlyContinue)
    $servers | Stop-Process -Force -ErrorAction SilentlyContinue
    foreach ($p in $servers) { $p.WaitForExit(5000) | Out-Null }
    if (Get-Process -Name 'ollama' -ErrorAction SilentlyContinue) { throw 'Ollama still running before smoke test' }
    $proc = Start-Process -FilePath $exe -ArgumentList @('--selftest', '--check-cancellation') `
        -WorkingDirectory $testRoot -WindowStyle Hidden -Wait -PassThru `
        -RedirectStandardOutput (Join-Path $testRoot 'stdout.txt') -RedirectStandardError (Join-Path $testRoot 'stderr.txt')
    if ($proc.ExitCode -ne 0) { throw "Self-test failed ($($proc.ExitCode)). See $testRoot/stderr.txt" }
    if (Get-Process -Name 'ollama' -ErrorAction SilentlyContinue) { throw 'Ollama started during the embedded smoke test' }
    $translation = (Get-Content -LiteralPath (Join-Path $testRoot 'stdout.txt') -Raw -Encoding UTF8).Trim()
    $result = [ordered]@{
        TestFolder = $testRoot
        ExitCode = $proc.ExitCode
        Translation = $translation
        OllamaWasRunning = ($ollamaProcesses.Count -gt 0)
        OllamaUsedDuringTest = $false
        CompletedAt = [DateTimeOffset]::Now.ToString('O')
    }
    $result | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $PSScriptRoot '../artifacts/smoke-test.json') -Encoding UTF8
    $result | ConvertTo-Json
}
finally {
    if ($appPath) { Start-Process -FilePath $appPath -WindowStyle Hidden }
    elseif ($serverPath) { Start-Process -FilePath $serverPath -ArgumentList 'serve' -WindowStyle Hidden }
    Write-Output "Smoke-test files retained at $testRoot"
}
