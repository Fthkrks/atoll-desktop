# Feature: "Model konumu" (model folder) setting and an app-managed Ollama server

## Problem
On the user's machine, the Ollama app looks for models in the wrong folder (`C:\Users\asus\.ollama\models`), but the models live in `D:\ollama\models`. Measured: when `ollama serve` is started with `OLLAMA_MODELS=D:\ollama\models`, the models appear without trouble. The user wants to type the model folder inside the app and have **the app start Ollama with that location**.

## Design
When the user enters a model folder, the app starts **its own** `ollama serve` process on a **separate port** with `OLLAMA_MODELS=<folder>`. It does NOT stop or touch the Ollama app the user already has running on 11434. When the folder is empty, the old behaviour stays (`OllamaUrl` is used).

### 1. AppSettings (new fields; old settings.json files must still load, new fields are optional)
```csharp
public string ModelsPath { get; set; } = "";          // empty = do not manage, use OllamaUrl
public string OllamaExePath { get; set; } = "";       // empty = auto-detect
public int ManagedPort { get; set; } = 11435;
```

### 2. `HyTranslate.Core/Ollama/OllamaServerManager.cs` (new)
- `string? FindOllamaExe(string? configured)`: if `configured` exists, use it. Otherwise: `%LOCALAPPDATA%\Programs\Ollama\ollama.exe`, `%ProgramFiles%\Ollama\ollama.exe`, then search `ollama.exe` on PATH. If none is found, return null.
- `Task<ServerStartResult> StartAsync(string modelsPath, int port, string? exePath, CancellationToken ct)`:
  - Validation: if `modelsPath` does not exist, return an error ("Klasör bulunamadı"). The folder may be empty (a model can be downloaded into it); only warn when there is no `manifests` subfolder ("Bu klasörde henüz model yok; Ayarlar'dan indirebilirsiniz").
  - If something is already listening on the port: if `GET http://127.0.0.1:{port}/api/version` answers, call `/api/tags`. If it is our server from an earlier session it can be reused, but to be safe: if THIS process did not start it, return the error "Port {port} başka bir uygulama tarafından kullanılıyor" and suggest another port. (Simple rule: one we did not start = in use.)
  - Start the process: `ollama.exe serve`, `UseShellExecute=false`, `CreateNoWindow=true`, `RedirectStandardOutput/Error=true`. Environment: `OLLAMA_MODELS=<modelsPath>`, `OLLAMA_HOST=127.0.0.1:{port}`, and also `OLLAMA_KEEP_ALIVE=30m`. Append stdout/stderr asynchronously to `%LOCALAPPDATA%\HyTranslate\ollama-server.log` (with a timestamp; when the file exceeds 5 MB, truncate it at startup).
  - Add the process to a Windows **Job Object** with `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE` (P/Invoke: CreateJobObject, SetInformationJobObject, AssignProcessToJobObject). That way the server shuts down even if the app crashes.
  - Readiness: poll `/api/version` every 300 ms, for up to 20 s. If the process exits early, return the last 20 log lines as the error.
  - Return: `ServerStartResult { bool Success; string BaseUrl; string? Error; string? Warning; }`
- `Task StopAsync()`: kill the process it started (`Kill(entireProcessTree:true)`) and dispose it.
- `bool IsRunning`, `string? CurrentModelsPath`, `int? CurrentPort`.
- `IDisposable`.

### 3. OllamaClient must be reconfigurable
It is currently a singleton built with the URL at startup. Add `void Reconfigure(string baseUrl, string modelName)` (thread-safe; requests made afterwards go to the new address). The model name can change from settings too.

### 4. App startup (`App.xaml.cs`)
- If `ModelsPath` is set: after the window opens (without blocking the UI), call `OllamaServerManager.StartAsync`. On success, `OllamaClient.Reconfigure(result.BaseUrl, settings.ModelName)`. Then run the existing "is Ollama/the model installed" check (that warning must not appear before the server has started).
- While it starts, the main page InfoBar (Informational) says "Ollama başlatılıyor… (model klasörü: D:\ollama\models)". On error, show an InfoBar Error with the message and an "Ayarları Aç" button.
- If `ModelsPath` is empty: `Reconfigure(settings.OllamaUrl, ...)`, as before.
- On app close (`MainWindow.Closed`), call `StopAsync()`.

### 5. Settings page: a new "Model konumu" card (at the top of the Ollama card)
- `TextBox` Header="Model konumu (OLLAMA_MODELS)", PlaceholderText="Örn. D:\ollama\models — boş bırakılırsa çalışan Ollama kullanılır", plus a **"Gözat…"** button (`FolderPicker`, initialized with `InitializeWithWindow` for the unpackaged app).
- `NumberBox` Header="Uygulamanın Ollama portu", default 11435.
- `TextBox` Header="ollama.exe yolu (isteğe bağlı)" + Gözat (FileOpenPicker, .exe). When empty, show the auto-detected path as a hint below it ("Bulunan: C:\...\ollama.exe" or "ollama.exe bulunamadı").
- **"Kaydet ve Ollama'yı başlat"** button: save the settings → if a server is running, stop it → start it with the new folder → Reconfigure → run the connection test automatically. While this runs, show a ProgressRing and disable the button.
- Status line: "● Çalışıyor — 127.0.0.1:11435, klasör: D:\ollama\models" (green) / "● Durduruldu" / error (red). If the field is cleared and saved, stop the managed server and go back to `OllamaUrl`.
- When ModelsPath is set, the "Ollama Adresi" field is disabled (IsEnabled=false, with the note "Model konumu ayarlıyken uygulama kendi sunucusunu kullanır").
- **Model selection**: replace the model name TextBox with an editable `ComboBox` (IsEditable=true), filled with the installed models from `/api/tags` (refreshed after the connection test and after a server start). The user can still type a name by hand.
- "Modeli indir" downloads through the managed server, so the model goes into the chosen folder. Keep it that way.

### 6. Tests (`tests/HyTranslate.Core.Tests`)
- `FindOllamaExe`: a configured path that exists is returned; one that does not exist falls back to auto-detection (testable with a temporary fake .exe file).
- `StartAsync` with a folder that does not exist → Success=false with an error message (do not start a real server).
- `AppSettings`: an old JSON file (without the new fields) loads with ModelsPath = "" and ManagedPort = 11435.
- `OllamaClient.Reconfigure`: requests made afterwards go to the new base URL (fake `HttpMessageHandler`).

## Acceptance
- `dotnet build HyTranslate.sln -c Debug -p:Platform=x64` → 0 errors
- `dotnet test tests/HyTranslate.Core.Tests -p:Platform=x64` → all pass
- Update the README (Turkish): add a "Model konumu" section.
