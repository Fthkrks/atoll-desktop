using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HyTranslate.Core.Ocr;
using HyTranslate.Core.Ollama;
using HyTranslate.Core.Settings;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace HyTranslate.App.ViewModels;

public record OcrLanguageOption(string Tag, string DisplayName);

public class SettingsViewModel : ObservableObject
{
    private static readonly SolidColorBrush GreenBrush = new(Color.FromArgb(255, 16, 124, 16));
    private static readonly SolidColorBrush RedBrush = new(Color.FromArgb(255, 196, 43, 28));
    private static readonly SolidColorBrush GrayBrush = new(Color.FromArgb(255, 120, 120, 120));

    private readonly SettingsManager _settingsManager;
    private readonly OllamaClient _ollamaClient;
    private readonly OllamaServerManager _serverManager;
    private readonly DispatcherQueue _dispatcherQueue;

    private string _modelsPath = string.Empty;
    private int _managedPort = 11435;
    private string _ollamaExePath = string.Empty;

    private string _ollamaUrl = "http://localhost:11434";
    private string _modelName = "maternion/hy-mt2:1.8b";
    private string _theme = "System";

    private bool _isStartingServer;
    private string _serverStatusText = "● Durduruldu";
    private SolidColorBrush _serverStatusBrush = GrayBrush;

    private bool _isTesting;
    private bool _hasTested;
    private bool _isConnectionSuccessful;
    private string _testResultMessage = string.Empty;

    private bool _isDownloading;
    private double _downloadProgress;
    private string _downloadStatusText = string.Empty;

    public ObservableCollection<string> AvailableModels { get; } = new();

    private bool _ocrEnabled = true;
    private OcrLanguageOption? _selectedOcrLanguageItem;
    private Visibility _noOcrLanguagesNoteVisibility = Visibility.Collapsed;

    public bool OcrEnabled
    {
        get => _ocrEnabled;
        set => SetProperty(ref _ocrEnabled, value);
    }

    public OcrLanguageOption? SelectedOcrLanguageItem
    {
        get => _selectedOcrLanguageItem;
        set => SetProperty(ref _selectedOcrLanguageItem, value);
    }

    public ObservableCollection<OcrLanguageOption> OcrLanguagesList { get; } = new();

    public Visibility NoOcrLanguagesNoteVisibility
    {
        get => _noOcrLanguagesNoteVisibility;
        set => SetProperty(ref _noOcrLanguagesNoteVisibility, value);
    }

    public string ModelsPath
    {
        get => _modelsPath;
        set
        {
            if (SetProperty(ref _modelsPath, value))
            {
                OnPropertyChanged(nameof(IsOllamaUrlEnabled));
                OnPropertyChanged(nameof(OllamaUrlNoteVisibility));
            }
        }
    }

    public int ManagedPort
    {
        get => _managedPort;
        set
        {
            if (SetProperty(ref _managedPort, value))
            {
                OnPropertyChanged(nameof(ManagedPortDouble));
            }
        }
    }

    public double ManagedPortDouble
    {
        get => _managedPort;
        set
        {
            if (!double.IsNaN(value) && value >= 1024 && value <= 65535)
            {
                ManagedPort = (int)value;
            }
        }
    }

    public string OllamaExePath
    {
        get => _ollamaExePath;
        set
        {
            if (SetProperty(ref _ollamaExePath, value))
            {
                OnPropertyChanged(nameof(OllamaExeHint));
            }
        }
    }

    public string OllamaExeHint
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(OllamaExePath))
                return string.Empty;

            var detected = OllamaServerManager.FindOllamaExeStatic(null);
            return !string.IsNullOrEmpty(detected)
                ? $"Bulunan: {detected}"
                : "ollama.exe bulunamadı";
        }
    }

    public bool IsOllamaUrlEnabled => string.IsNullOrWhiteSpace(ModelsPath);

    public Visibility OllamaUrlNoteVisibility =>
        string.IsNullOrWhiteSpace(ModelsPath) ? Visibility.Collapsed : Visibility.Visible;

    public string OllamaUrl
    {
        get => _ollamaUrl;
        set => SetProperty(ref _ollamaUrl, value);
    }

    public string ModelName
    {
        get => _modelName;
        set => SetProperty(ref _modelName, value);
    }

    public string Theme
    {
        get => _theme;
        set
        {
            if (SetProperty(ref _theme, value))
            {
                ApplyTheme(value);
            }
        }
    }

    public bool IsStartingServer
    {
        get => _isStartingServer;
        set => SetProperty(ref _isStartingServer, value);
    }

    public string ServerStatusText
    {
        get => _serverStatusText;
        set => SetProperty(ref _serverStatusText, value);
    }

    public SolidColorBrush ServerStatusBrush
    {
        get => _serverStatusBrush;
        set => SetProperty(ref _serverStatusBrush, value);
    }

    public bool IsTesting
    {
        get => _isTesting;
        set => SetProperty(ref _isTesting, value);
    }

    public bool HasTested
    {
        get => _hasTested;
        set => SetProperty(ref _hasTested, value);
    }

    public bool IsConnectionSuccessful
    {
        get => _isConnectionSuccessful;
        set => SetProperty(ref _isConnectionSuccessful, value);
    }

    public string TestResultMessage
    {
        get => _testResultMessage;
        set => SetProperty(ref _testResultMessage, value);
    }

    public bool IsDownloading
    {
        get => _isDownloading;
        set => SetProperty(ref _isDownloading, value);
    }

    public double DownloadProgress
    {
        get => _downloadProgress;
        set => SetProperty(ref _downloadProgress, value);
    }

    public string DownloadStatusText
    {
        get => _downloadStatusText;
        set => SetProperty(ref _downloadStatusText, value);
    }

    public IAsyncRelayCommand SaveAndStartServerCommand { get; }
    public IAsyncRelayCommand TestConnectionCommand { get; }
    public IAsyncRelayCommand DownloadModelCommand { get; }
    public IAsyncRelayCommand SaveSettingsCommand { get; }

    public SettingsViewModel(
        SettingsManager settingsManager,
        OllamaClient ollamaClient,
        OllamaServerManager serverManager)
    {
        _settingsManager = settingsManager ?? throw new ArgumentNullException(nameof(settingsManager));
        _ollamaClient = ollamaClient ?? throw new ArgumentNullException(nameof(ollamaClient));
        _serverManager = serverManager ?? throw new ArgumentNullException(nameof(serverManager));
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();

        SaveAndStartServerCommand = new AsyncRelayCommand(SaveAndStartServerAsync);
        TestConnectionCommand = new AsyncRelayCommand(TestConnectionAsync);
        DownloadModelCommand = new AsyncRelayCommand(DownloadModelAsync);
        SaveSettingsCommand = new AsyncRelayCommand(SaveSettingsAsync);
    }

    public async Task InitializeAsync()
    {
        var settings = await _settingsManager.LoadSettingsAsync().ConfigureAwait(false);
        UpdateUi(() =>
        {
            OllamaUrl = settings.OllamaUrl;
            ModelName = settings.ModelName;
            Theme = settings.Theme;
            ModelsPath = settings.ModelsPath;
            ManagedPort = settings.ManagedPort;
            OllamaExePath = settings.OllamaExePath;
            OcrEnabled = settings.OcrEnabled;

            OcrLanguagesList.Clear();
            var autoOption = new OcrLanguageOption("", "Otomatik");
            OcrLanguagesList.Add(autoOption);

            var available = OcrLanguages.Available();
            if (available.Count == 0)
            {
                NoOcrLanguagesNoteVisibility = Visibility.Visible;
            }
            else
            {
                NoOcrLanguagesNoteVisibility = Visibility.Collapsed;
                foreach (var (tag, name) in available)
                {
                    OcrLanguagesList.Add(new OcrLanguageOption(tag, name));
                }
            }

            var selected = OcrLanguagesList.FirstOrDefault(o => string.Equals(o.Tag, settings.OcrLanguage, StringComparison.OrdinalIgnoreCase)) ?? autoOption;
            SelectedOcrLanguageItem = selected;

            if (_serverManager.IsRunning)
            {
                ServerStatusText = $"● Çalışıyor — 127.0.0.1:{_serverManager.CurrentPort}, klasör: {_serverManager.CurrentModelsPath}";
                ServerStatusBrush = GreenBrush;
            }
            else
            {
                ServerStatusText = "● Durduruldu";
                ServerStatusBrush = GrayBrush;
            }
        });

        await RefreshAvailableModelsAsync().ConfigureAwait(false);
    }

    public async Task SaveAndStartServerAsync()
    {
        IsStartingServer = true;
        try
        {
            var currentSettings = await _settingsManager.LoadSettingsAsync().ConfigureAwait(false);
            var settings = new AppSettings
            {
                OllamaUrl = OllamaUrl,
                ModelName = ModelName,
                Theme = Theme,
                ModelsPath = ModelsPath,
                ManagedPort = ManagedPort,
                OllamaExePath = OllamaExePath,
                TranslateSheetNames = currentSettings.TranslateSheetNames,
                DefaultSourceLang = currentSettings.DefaultSourceLang,
                DefaultTargetLang = currentSettings.DefaultTargetLang,
                LastOutputFolder = currentSettings.LastOutputFolder,
                OcrEnabled = OcrEnabled,
                OcrLanguage = SelectedOcrLanguageItem?.Tag ?? ""
            };
            await _settingsManager.SaveSettingsAsync(settings).ConfigureAwait(false);

            if (_serverManager.IsRunning)
            {
                await _serverManager.StopAsync().ConfigureAwait(false);
            }

            if (string.IsNullOrWhiteSpace(ModelsPath))
            {
                _ollamaClient.Reconfigure(OllamaUrl, ModelName);
                UpdateUi(() =>
                {
                    ServerStatusText = "● Durduruldu";
                    ServerStatusBrush = GrayBrush;
                });
                await TestConnectionAsync().ConfigureAwait(false);
            }
            else
            {
                var result = await _serverManager.StartAsync(ModelsPath, ManagedPort, OllamaExePath).ConfigureAwait(false);
                if (result.Success)
                {
                    _ollamaClient.Reconfigure(result.BaseUrl, ModelName);
                    UpdateUi(() =>
                    {
                        ServerStatusText = $"● Çalışıyor — 127.0.0.1:{ManagedPort}, klasör: {ModelsPath}";
                        ServerStatusBrush = GreenBrush;
                    });
                    await TestConnectionAsync().ConfigureAwait(false);
                }
                else
                {
                    UpdateUi(() =>
                    {
                        ServerStatusText = $"● Hata: {result.Error}";
                        ServerStatusBrush = RedBrush;
                    });
                }
            }
        }
        catch (Exception ex)
        {
            UpdateUi(() =>
            {
                ServerStatusText = $"● Hata: {ex.Message}";
                ServerStatusBrush = RedBrush;
            });
        }
        finally
        {
            UpdateUi(() =>
            {
                IsStartingServer = false;
            });
        }
    }

    public async Task TestConnectionAsync()
    {
        IsTesting = true;
        HasTested = false;
        TestResultMessage = "Bağlantı kontrol ediliyor...";

        try
        {
            bool isAlive = await _ollamaClient.IsAliveAsync().ConfigureAwait(false);

            if (!isAlive)
            {
                UpdateUi(() =>
                {
                    IsConnectionSuccessful = false;
                    TestResultMessage = "✗ Ollama sunucusuna ulaşılamadı. `ollama serve` ile başlatın.";
                    HasTested = true;
                });
                return;
            }

            bool hasModel = await _ollamaClient.HasModelAsync(ModelName).ConfigureAwait(false);
            UpdateUi(() =>
            {
                if (hasModel)
                {
                    IsConnectionSuccessful = true;
                    TestResultMessage = $"✓ Ollama çalışıyor / ✓ Model '{ModelName}' yüklü.";
                }
                else
                {
                    IsConnectionSuccessful = false;
                    TestResultMessage = $"✓ Ollama çalışıyor / ✗ Model '{ModelName}' henüz indirilmemiş.";
                }
                HasTested = true;
            });

            await RefreshAvailableModelsAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            UpdateUi(() =>
            {
                IsConnectionSuccessful = false;
                TestResultMessage = $"✗ Hata: {ex.Message}";
                HasTested = true;
            });
        }
        finally
        {
            UpdateUi(() =>
            {
                IsTesting = false;
            });
        }
    }

    public async Task DownloadModelAsync()
    {
        IsDownloading = true;
        DownloadProgress = 0;
        DownloadStatusText = "İndirme başlatılıyor...";

        try
        {
            await foreach (var progress in _ollamaClient.PullModelAsync(ModelName).ConfigureAwait(false))
            {
                var p = progress;
                UpdateUi(() =>
                {
                    DownloadProgress = p.Percent;
                    DownloadStatusText = $"{p.Status} - %{p.Percent:F0}";
                });
            }

            UpdateUi(() =>
            {
                DownloadProgress = 100;
                DownloadStatusText = "✓ Model başarıyla indirildi!";
                IsConnectionSuccessful = true;
                TestResultMessage = $"✓ Ollama çalışıyor / ✓ Model '{ModelName}' yüklü.";
                HasTested = true;
            });

            await RefreshAvailableModelsAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            UpdateUi(() =>
            {
                DownloadStatusText = $"İndirme hatası: {ex.Message}";
            });
        }
        finally
        {
            UpdateUi(() =>
            {
                IsDownloading = false;
            });
        }
    }

    public async Task RefreshAvailableModelsAsync()
    {
        try
        {
            var models = await _ollamaClient.ListModelsAsync().ConfigureAwait(false);
            UpdateUi(() =>
            {
                AvailableModels.Clear();
                foreach (var m in models)
                {
                    AvailableModels.Add(m);
                }
            });
        }
        catch
        {
            // Ignore error refreshing models
        }
    }

    public async Task SaveSettingsAsync()
    {
        var currentSettings = await _settingsManager.LoadSettingsAsync().ConfigureAwait(false);
        var settings = new AppSettings
        {
            OllamaUrl = OllamaUrl,
            ModelName = ModelName,
            Theme = Theme,
            ModelsPath = ModelsPath,
            ManagedPort = ManagedPort,
            OllamaExePath = OllamaExePath,
            TranslateSheetNames = currentSettings.TranslateSheetNames,
            DefaultSourceLang = currentSettings.DefaultSourceLang,
            DefaultTargetLang = currentSettings.DefaultTargetLang,
            LastOutputFolder = currentSettings.LastOutputFolder,
            OcrEnabled = OcrEnabled,
            OcrLanguage = SelectedOcrLanguageItem?.Tag ?? ""
        };
        await _settingsManager.SaveSettingsAsync(settings).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(ModelsPath))
        {
            _ollamaClient.Reconfigure(OllamaUrl, ModelName);
        }
    }

    private void ApplyTheme(string themeName)
    {
        UpdateUi(() =>
        {
            if (App.CurrentWindow?.Content is FrameworkElement rootElement)
            {
                rootElement.RequestedTheme = themeName switch
                {
                    "Light" => ElementTheme.Light,
                    "Dark" => ElementTheme.Dark,
                    _ => ElementTheme.Default
                };
            }
        });
    }

    private void UpdateUi(Action action)
    {
        if (_dispatcherQueue != null && !_dispatcherQueue.HasThreadAccess)
        {
            _dispatcherQueue.TryEnqueue(() => action());
        }
        else
        {
            action();
        }
    }
}
