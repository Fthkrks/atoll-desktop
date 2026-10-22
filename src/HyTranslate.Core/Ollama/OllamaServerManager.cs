using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace HyTranslate.Core.Ollama;

public class ServerStartResult
{
    public bool Success { get; set; }
    public string BaseUrl { get; set; } = string.Empty;
    public string? Error { get; set; }
    public string? Warning { get; set; }
}

public class OllamaServerManager : IDisposable
{
    private Process? _process;
    private IntPtr _jobHandle = IntPtr.Zero;
    private readonly object _stateLock = new();
    private readonly object _logLock = new();
    private readonly List<string> _recentLogs = new();

    private bool _isRunning;
    private string? _currentModelsPath;
    private int? _currentPort;

    public bool IsRunning
    {
        get
        {
            lock (_stateLock)
            {
                return _isRunning && _process != null && !_process.HasExited;
            }
        }
    }

    public string? CurrentModelsPath
    {
        get
        {
            lock (_stateLock) return _currentModelsPath;
        }
    }

    public int? CurrentPort
    {
        get
        {
            lock (_stateLock) return _currentPort;
        }
    }

    public string? FindOllamaExe(string? configured) => FindOllamaExeStatic(configured);

    public static string? FindOllamaExeStatic(
        string? configured = null,
        string? localAppDataOverride = null,
        string? progFilesOverride = null,
        string? pathOverride = null)
    {
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
        {
            return Path.GetFullPath(configured);
        }

        var localAppData = localAppDataOverride ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrEmpty(localAppData))
        {
            var localCandidate = Path.Combine(localAppData, "Programs", "Ollama", "ollama.exe");
            if (File.Exists(localCandidate))
            {
                return Path.GetFullPath(localCandidate);
            }
        }

        var progFiles = progFilesOverride ?? Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (!string.IsNullOrEmpty(progFiles))
        {
            var progCandidate = Path.Combine(progFiles, "Ollama", "ollama.exe");
            if (File.Exists(progCandidate))
            {
                return Path.GetFullPath(progCandidate);
            }
        }

        var pathEnv = pathOverride ?? Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrEmpty(pathEnv))
        {
            foreach (var part in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                try
                {
                    var dir = part.Trim();
                    var exeCandidate = Path.Combine(dir, "ollama.exe");
                    if (File.Exists(exeCandidate))
                    {
                        return Path.GetFullPath(exeCandidate);
                    }

                    if (!OperatingSystem.IsWindows())
                    {
                        var binCandidate = Path.Combine(dir, "ollama");
                        if (File.Exists(binCandidate))
                        {
                            return Path.GetFullPath(binCandidate);
                        }
                    }
                }
                catch
                {
                    // Ignore malformed path entries
                }
            }
        }

        return null;
    }

    public async Task<ServerStartResult> StartAsync(
        string modelsPath,
        int port,
        string? exePath = null,
        CancellationToken ct = default)
    {
        // 1. Validation: modelsPath
        if (string.IsNullOrWhiteSpace(modelsPath) || !Directory.Exists(modelsPath))
        {
            return new ServerStartResult
            {
                Success = false,
                Error = "Klasör bulunamadı"
            };
        }

        string? warning = null;
        var manifestsPath = Path.Combine(modelsPath, "manifests");
        if (!Directory.Exists(manifestsPath))
        {
            warning = "Bu klasörde henüz model yok; Ayarlar'dan indirebilirsiniz";
        }

        // 2. Resolve executable
        var resolvedExe = FindOllamaExe(exePath);
        if (string.IsNullOrEmpty(resolvedExe))
        {
            return new ServerStartResult
            {
                Success = false,
                Error = "ollama.exe bulunamadı"
            };
        }

        // 3. Check if we already started a server on this port
        lock (_stateLock)
        {
            if (_isRunning && _process != null && !_process.HasExited && _currentPort == port)
            {
                if (string.Equals(_currentModelsPath, modelsPath, StringComparison.OrdinalIgnoreCase))
                {
                    return new ServerStartResult
                    {
                        Success = true,
                        BaseUrl = $"http://127.0.0.1:{port}",
                        Warning = warning
                    };
                }
            }
        }

        // Stop our own previous server if running
        await StopAsync().ConfigureAwait(false);

        // 4. Check if something else is listening on the port
        if (await CheckPortInUseAsync(port, ct).ConfigureAwait(false))
        {
            return new ServerStartResult
            {
                Success = false,
                Error = $"Port {port} başka bir uygulama tarafından kullanılıyor. Lütfen başka bir port seçin."
            };
        }

        // 5. Setup log file (%LOCALAPPDATA%\HyTranslate\ollama-server.log)
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var appFolder = Path.Combine(localAppData, "HyTranslate");
        Directory.CreateDirectory(appFolder);
        var logPath = Path.Combine(appFolder, "ollama-server.log");

        try
        {
            if (File.Exists(logPath))
            {
                var fi = new FileInfo(logPath);
                if (fi.Length > 5 * 1024 * 1024)
                {
                    File.WriteAllText(logPath, string.Empty);
                }
            }
        }
        catch
        {
            // Truncation non-critical
        }

        lock (_logLock)
        {
            _recentLogs.Clear();
        }

        // 6. Configure process
        var psi = new ProcessStartInfo
        {
            FileName = resolvedExe,
            Arguments = "serve",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        psi.Environment["OLLAMA_MODELS"] = modelsPath;
        psi.Environment["OLLAMA_HOST"] = $"127.0.0.1:{port}";
        psi.Environment["OLLAMA_KEEP_ALIVE"] = "30m";

        var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

        process.OutputDataReceived += (s, e) => AppendLogLine("stdout", e.Data, logPath);
        process.ErrorDataReceived += (s, e) => AppendLogLine("stderr", e.Data, logPath);

        try
        {
            if (!process.Start())
            {
                return new ServerStartResult
                {
                    Success = false,
                    Error = "Ollama işlemi başlatılamadı."
                };
            }
        }
        catch (Exception ex)
        {
            return new ServerStartResult
            {
                Success = false,
                Error = $"Ollama başlatma hatası: {ex.Message}"
            };
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        // 7. Add to Windows Job Object
        if (OperatingSystem.IsWindows())
        {
            try
            {
                if (_jobHandle == IntPtr.Zero)
                {
                    _jobHandle = WindowsJobObject.CreateJobWithKillOnClose();
                }
                if (_jobHandle != IntPtr.Zero)
                {
                    WindowsJobObject.AssignProcess(_jobHandle, process.Handle);
                }
            }
            catch
            {
                // Job object non-critical
            }
        }

        // 8. Poll readiness on /api/version every 300 ms for up to 20 s
        using var readinessClient = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var startTime = DateTime.UtcNow;
        var versionUrl = $"http://127.0.0.1:{port}/api/version";

        while (DateTime.UtcNow - startTime < TimeSpan.FromSeconds(20))
        {
            ct.ThrowIfCancellationRequested();

            if (process.HasExited)
            {
                string last20Lines;
                lock (_logLock)
                {
                    last20Lines = string.Join(Environment.NewLine, _recentLogs.TakeLast(20));
                }
                return new ServerStartResult
                {
                    Success = false,
                    Error = string.IsNullOrWhiteSpace(last20Lines)
                        ? $"Ollama beklenmedik şekilde kapandı (Çıkış kodu: {process.ExitCode})."
                        : last20Lines
                };
            }

            try
            {
                using var resp = await readinessClient.GetAsync(versionUrl, ct).ConfigureAwait(false);
                if (resp.IsSuccessStatusCode)
                {
                    lock (_stateLock)
                    {
                        _process = process;
                        _isRunning = true;
                        _currentPort = port;
                        _currentModelsPath = modelsPath;
                    }

                    return new ServerStartResult
                    {
                        Success = true,
                        BaseUrl = $"http://127.0.0.1:{port}",
                        Warning = warning
                    };
                }
            }
            catch
            {
                // Not ready yet
            }

            try
            {
                await Task.Delay(300, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                throw;
            }
        }

        // Timed out
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch { }

        string timeoutLogs;
        lock (_logLock)
        {
            timeoutLogs = string.Join(Environment.NewLine, _recentLogs.TakeLast(20));
        }

        return new ServerStartResult
        {
            Success = false,
            Error = string.IsNullOrWhiteSpace(timeoutLogs)
                ? "Ollama 20 saniye içinde yanıt vermedi."
                : timeoutLogs
        };
    }

    public async Task StopAsync()
    {
        Process? proc;
        lock (_stateLock)
        {
            proc = _process;
            _process = null;
            _isRunning = false;
            _currentModelsPath = null;
            _currentPort = null;
        }

        if (proc != null)
        {
            try
            {
                if (!proc.HasExited)
                {
                    proc.Kill(entireProcessTree: true);
                    await proc.WaitForExitAsync().ConfigureAwait(false);
                }
            }
            catch
            {
                // Process already terminated
            }
            finally
            {
                proc.Dispose();
            }
        }

        if (_jobHandle != IntPtr.Zero)
        {
            WindowsJobObject.Close(_jobHandle);
            _jobHandle = IntPtr.Zero;
        }
    }

    private void AppendLogLine(string streamName, string? line, string logPath)
    {
        if (line == null) return;
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        var formatted = $"[{timestamp}] [{streamName}] {line}";
        lock (_logLock)
        {
            _recentLogs.Add(formatted);
            if (_recentLogs.Count > 100)
            {
                _recentLogs.RemoveAt(0);
            }
            try
            {
                File.AppendAllText(logPath, formatted + Environment.NewLine);
            }
            catch
            {
                // File access conflict or permissions
            }
        }
    }

    private static async Task<bool> CheckPortInUseAsync(int port, CancellationToken ct)
    {
        try
        {
            using var tcp = new System.Net.Sockets.TcpClient();
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromMilliseconds(500));
            await tcp.ConnectAsync("127.0.0.1", port, cts.Token).ConfigureAwait(false);
            return true;
        }
        catch
        {
            // TCP connect failed
        }

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromMilliseconds(500) };
            using var resp = await http.GetAsync($"http://127.0.0.1:{port}/api/version", ct).ConfigureAwait(false);
            return true;
        }
        catch
        {
            // HTTP get failed
        }

        return false;
    }

    public void Dispose()
    {
        try
        {
            StopAsync().GetAwaiter().GetResult();
        }
        catch { }

        GC.SuppressFinalize(this);
    }
}

internal static class WindowsJobObject
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(
        IntPtr hJob,
        int JobObjectInfoClass,
        IntPtr lpJobObjectInfo,
        uint cbJobObjectInfoLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    private const int JobObjectExtendedLimitInformation = 9;
    private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryLimit;
        public UIntPtr PeakJobMemoryLimit;
    }

    public static IntPtr CreateJobWithKillOnClose()
    {
        if (!OperatingSystem.IsWindows()) return IntPtr.Zero;
        try
        {
            IntPtr hJob = CreateJobObject(IntPtr.Zero, null);
            if (hJob == IntPtr.Zero) return IntPtr.Zero;

            var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
            {
                BasicLimitInformation = new JOBOBJECT_BASIC_LIMIT_INFORMATION
                {
                    LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
                }
            };

            int length = Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
            IntPtr pInfo = Marshal.AllocHGlobal(length);
            try
            {
                Marshal.StructureToPtr(info, pInfo, false);
                if (!SetInformationJobObject(hJob, JobObjectExtendedLimitInformation, pInfo, (uint)length))
                {
                    CloseHandle(hJob);
                    return IntPtr.Zero;
                }
            }
            finally
            {
                Marshal.FreeHGlobal(pInfo);
            }
            return hJob;
        }
        catch
        {
            return IntPtr.Zero;
        }
    }

    public static bool AssignProcess(IntPtr hJob, IntPtr hProcess)
    {
        if (!OperatingSystem.IsWindows() || hJob == IntPtr.Zero || hProcess == IntPtr.Zero)
            return false;
        try
        {
            return AssignProcessToJobObject(hJob, hProcess);
        }
        catch
        {
            return false;
        }
    }

    public static void Close(IntPtr hJob)
    {
        if (OperatingSystem.IsWindows() && hJob != IntPtr.Zero)
        {
            try
            {
                CloseHandle(hJob);
            }
            catch { }
        }
    }
}
