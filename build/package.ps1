[CmdletBinding()]
param(
    [string]$ModelPath = 'C:\Users\asus\.ollama\models\blobs\sha256-dc5f44fcf1fa496ee7ad725982c0c8c553a4de00259b53af84c4b89fb0c06699',
    [string]$VcRuntimeDirectory = "$env:WINDIR\System32",
    [switch]$KeepStaging
)
$ErrorActionPreference = 'Stop'
$desktopRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$artifacts = Join-Path $desktopRoot 'artifacts'
$stageRoot = Join-Path $artifacts ('package-' + [Guid]::NewGuid().ToString('N'))
$appFolder = Join-Path $stageRoot 'Atoll'
$zipPath = Join-Path $artifacts 'Atoll-Windows-x64.zip'
$expectedHash = 'DC5F44FCF1FA496EE7AD725982C0C8C553A4DE00259B53AF84C4B89FB0C06699'
if (!(Test-Path -LiteralPath $ModelPath -PathType Leaf)) { throw "Model not found: $ModelPath" }
if ((Get-FileHash -LiteralPath $ModelPath -Algorithm SHA256).Hash -ne $expectedHash) { throw 'The model does not match the licensed HY-MT2 Q4_K_M asset.' }
New-Item -ItemType Directory -Force $appFolder | Out-Null

try {
    & dotnet publish (Join-Path $desktopRoot 'src/HyTranslate.App/HyTranslate.App.csproj') `
        -c Release -r win-x64 --self-contained true -p:Platform=x64 `
        -p:WindowsAppSDKSelfContained=true -p:PublishReadyToRun=false `
        -p:DebugType=None -p:DebugSymbols=false -o $appFolder --nologo
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }

    # The CPU package contains several architectures. Keep Windows x64 CPU variants for automatic AVX selection.
    $runtimeFolder = Join-Path $appFolder 'runtimes'
    if (Test-Path -LiteralPath $runtimeFolder) {
        Get-ChildItem -LiteralPath $runtimeFolder -Directory | Where-Object Name -ne 'win-x64' | ForEach-Object {
            $target = [IO.Path]::GetFullPath($_.FullName)
            if (!$target.StartsWith($appFolder + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe runtime cleanup path' }
            Remove-Item -LiteralPath $target -Recurse -Force
        }
    }
    Get-ChildItem -LiteralPath $appFolder -Recurse -File -Filter '*.pdb' | Remove-Item -Force
    New-Item -ItemType Directory -Force (Join-Path $appFolder 'model') | Out-Null
    Copy-Item -LiteralPath $ModelPath -Destination (Join-Path $appFolder 'model/hy-mt2-1.8b-q4_k_m.gguf')
    $licenseFolder = Join-Path $appFolder 'LICENSES'
    New-Item -ItemType Directory -Force $licenseFolder | Out-Null
    Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'LICENSES') -File | Copy-Item -Destination $licenseFolder
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README.txt') -Destination $appFolder

    # App-local release CRT files: end users do not have to run a VC++ installer.
    foreach ($name in @('msvcp140.dll', 'msvcp140_1.dll', 'msvcp140_2.dll', 'msvcp140_atomic_wait.dll', 'msvcp140_codecvt_ids.dll', 'vcruntime140.dll', 'vcruntime140_1.dll', 'concrt140.dll')) {
        $source = Join-Path $VcRuntimeDirectory $name
        if (!(Test-Path -LiteralPath $source)) { throw "Release VC runtime missing: $source" }
        if ((Get-AuthenticodeSignature -LiteralPath $source).Status -ne 'Valid') { throw "Unsigned VC runtime: $source" }
        Copy-Item -LiteralPath $source -Destination $appFolder
    }

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
    $archive = [IO.Compression.ZipFile]::Open($zipPath, [IO.Compression.ZipArchiveMode]::Create)
    try {
        Get-ChildItem -LiteralPath $appFolder -Recurse -File | ForEach-Object {
            # ZIP paths always use '/', including when packaging with Windows PowerShell 5.1.
            $entryName = $_.FullName.Substring($stageRoot.Length + 1).Replace('\', '/')
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $_.FullName, $entryName, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
    }
    finally { $archive.Dispose() }
    $zipBytes = (Get-Item -LiteralPath $zipPath).Length
    if ($zipBytes -ge 2GB) { throw "Release asset must be smaller than 2 GiB: $zipBytes bytes" }
    $extractedBytes = (Get-ChildItem -LiteralPath $appFolder -Recurse -File | Measure-Object Length -Sum).Sum
    $result = [ordered]@{
        ZipPath = $zipPath
        ZipBytes = $zipBytes
        ExtractedBytes = [long]$extractedBytes
        RequiredFreeDiskBytes = [long]($zipBytes + $extractedBytes)
        SHA256 = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash
        ModelSHA256 = $expectedHash
        CreatedAt = [DateTimeOffset]::Now.ToString('O')
    }
    $result | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $artifacts 'package-info.json') -Encoding UTF8
    Write-Output "ZIP: $zipPath"
    Write-Output "Size: $zipBytes bytes ($([Math]::Round($zipBytes / 1GB, 3)) GiB)"
    Write-Output "Extracted: $extractedBytes bytes; free disk for download + extraction: $($zipBytes + $extractedBytes) bytes"
}
finally {
    if (!$KeepStaging -and (Test-Path -LiteralPath $stageRoot)) {
        $resolvedStage = [IO.Path]::GetFullPath($stageRoot)
        if (!$resolvedStage.StartsWith($artifacts + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or
            [IO.Path]::GetFileName($resolvedStage) -notmatch '^package-[0-9a-f]{32}$') { throw 'Unsafe package cleanup path' }
        Remove-Item -LiteralPath $resolvedStage -Recurse -Force
    }
}
