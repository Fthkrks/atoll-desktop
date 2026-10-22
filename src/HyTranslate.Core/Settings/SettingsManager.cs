using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace HyTranslate.Core.Settings;

public class SettingsManager
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _settingsFilePath;
    private readonly SemaphoreSlim _fileLock = new(1, 1);

    public SettingsManager(string? customPath = null)
    {
        if (!string.IsNullOrWhiteSpace(customPath))
        {
            _settingsFilePath = customPath;
        }
        else
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var appFolder = Path.Combine(localAppData, "HyTranslate");
            _settingsFilePath = Path.Combine(appFolder, "settings.json");
        }
    }

    public string SettingsFilePath => _settingsFilePath;

    public async Task<AppSettings> LoadSettingsAsync(CancellationToken ct = default)
    {
        await _fileLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_settingsFilePath))
            {
                var defaultSettings = new AppSettings();
                await SaveInternalAsync(defaultSettings, ct).ConfigureAwait(false);
                return defaultSettings;
            }

            var json = await File.ReadAllTextAsync(_settingsFilePath, ct).ConfigureAwait(false);
            var settings = JsonSerializer.Deserialize<AppSettings>(json);
            return settings ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task SaveSettingsAsync(AppSettings settings, CancellationToken ct = default)
    {
        await _fileLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await SaveInternalAsync(settings, ct).ConfigureAwait(false);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    private async Task SaveInternalAsync(AppSettings settings, CancellationToken ct)
    {
        var dir = Path.GetDirectoryName(_settingsFilePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var json = JsonSerializer.Serialize(settings, JsonOptions);
        await File.WriteAllTextAsync(_settingsFilePath, json, ct).ConfigureAwait(false);
    }
}
