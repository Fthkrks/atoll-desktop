using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HyTranslate.Core.Ollama;
using HyTranslate.Core.Settings;
using HyTranslate.Core.Translation;
using Microsoft.UI.Dispatching;

namespace HyTranslate.App.ViewModels;

public class MainViewModel : ObservableObject
{
    private readonly OllamaClient _ollamaClient;
    private readonly SettingsManager _settingsManager;
    private readonly OllamaServerManager _serverManager;
    private readonly DispatcherQueue _dispatcherQueue;
    private readonly LlamaSharpTranslator _embedded;

    private bool _showStartupWarning;
    private string _startupWarningMessage = string.Empty;
    private Microsoft.UI.Xaml.Controls.InfoBarSeverity _startupSeverity = Microsoft.UI.Xaml.Controls.InfoBarSeverity.Warning;
    private string _startupTitle = "Ollama Uyarısı";
    private bool _showSettingsButton = true;

    public bool ShowStartupWarning
    {
        get => _showStartupWarning;
        set => SetProperty(ref _showStartupWarning, value);
    }

    public string StartupWarningMessage
    {
        get => _startupWarningMessage;
        set => SetProperty(ref _startupWarningMessage, value);
    }

    public Microsoft.UI.Xaml.Controls.InfoBarSeverity StartupSeverity
    {
        get => _startupSeverity;
        set => SetProperty(ref _startupSeverity, value);
    }

    public string StartupTitle
    {
        get => _startupTitle;
        set => SetProperty(ref _startupTitle, value);
    }

    public bool ShowSettingsButton
    {
        get => _showSettingsButton;
        set => SetProperty(ref _showSettingsButton, value);
    }

    public IRelayCommand DismissWarningCommand { get; }

    public MainViewModel(
        OllamaClient ollamaClient,
        SettingsManager settingsManager,
        OllamaServerManager serverManager,
        LlamaSharpTranslator embedded)
    {
        _ollamaClient = ollamaClient ?? throw new ArgumentNullException(nameof(ollamaClient));
        _settingsManager = settingsManager ?? throw new ArgumentNullException(nameof(settingsManager));
        _serverManager = serverManager ?? throw new ArgumentNullException(nameof(serverManager));
        _embedded = embedded;
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();

        DismissWarningCommand = new RelayCommand(() => ShowStartupWarning = false);
    }

    public async Task CheckStartupStatusAsync()
    {
        try
        {
            var settings = await _settingsManager.LoadSettingsAsync().ConfigureAwait(false);

            if (settings.Backend != "Ollama")
            {
                UpdateUi(() =>
                {
                    StartupTitle = "Atoll";
                    StartupSeverity = Microsoft.UI.Xaml.Controls.InfoBarSeverity.Informational;
                    StartupWarningMessage = "Gömülü model yükleniyor…";
                    ShowStartupWarning = true;
                    ShowSettingsButton = false;
                });
                await _embedded.EnsureLoadedAsync().ConfigureAwait(false);
                UpdateUi(() => ShowStartupWarning = false);
                return;
            }

            if (!string.IsNullOrWhiteSpace(settings.ModelsPath))
            {
                UpdateUi(() =>
                {
                    StartupSeverity = Microsoft.UI.Xaml.Controls.InfoBarSeverity.Informational;
                    StartupTitle = "Ollama Başlatılıyor";
                    StartupWarningMessage = $"Ollama başlatılıyor… (model klasörü: {settings.ModelsPath})";
                    ShowSettingsButton = false;
                    ShowStartupWarning = true;
                });

                var startResult = await _serverManager.StartAsync(
                    settings.ModelsPath,
                    settings.ManagedPort,
                    settings.OllamaExePath).ConfigureAwait(false);

                if (!startResult.Success)
                {
                    UpdateUi(() =>
                    {
                        StartupSeverity = Microsoft.UI.Xaml.Controls.InfoBarSeverity.Error;
                        StartupTitle = "Ollama Başlatılamadı";
                        StartupWarningMessage = startResult.Error ?? "Ollama başlatılamadı.";
                        ShowSettingsButton = true;
                        ShowStartupWarning = true;
                    });
                    return;
                }

                _ollamaClient.Reconfigure(startResult.BaseUrl, settings.ModelName);

                if (!string.IsNullOrEmpty(startResult.Warning))
                {
                    UpdateUi(() =>
                    {
                        StartupSeverity = Microsoft.UI.Xaml.Controls.InfoBarSeverity.Warning;
                        StartupTitle = "Ollama Uyarısı";
                        StartupWarningMessage = startResult.Warning;
                        ShowSettingsButton = true;
                        ShowStartupWarning = true;
                    });
                    return;
                }
            }
            else
            {
                _ollamaClient.Reconfigure(settings.OllamaUrl, settings.ModelName);
            }

            var isAlive = await _ollamaClient.IsAliveAsync().ConfigureAwait(false);
            if (!isAlive)
            {
                UpdateUi(() =>
                {
                    StartupSeverity = Microsoft.UI.Xaml.Controls.InfoBarSeverity.Warning;
                    StartupTitle = "Ollama Uyarısı";
                    StartupWarningMessage = "Ollama çalışmıyor. Lütfen terminalden `ollama serve` ile başlatın.";
                    ShowSettingsButton = true;
                    ShowStartupWarning = true;
                });
                return;
            }

            var hasModel = await _ollamaClient.HasModelAsync().ConfigureAwait(false);
            if (!hasModel)
            {
                UpdateUi(() =>
                {
                    StartupSeverity = Microsoft.UI.Xaml.Controls.InfoBarSeverity.Warning;
                    StartupTitle = "Ollama Uyarısı";
                    StartupWarningMessage = $"'{_ollamaClient.ModelName}' modeli yüklü değil. Ayarlar sayfasından indirebilirsiniz.";
                    ShowSettingsButton = true;
                    ShowStartupWarning = true;
                });
                return;
            }

            UpdateUi(() =>
            {
                ShowStartupWarning = false;
            });
        }
        catch (Exception ex)
        {
            UpdateUi(() =>
            {
                StartupSeverity = Microsoft.UI.Xaml.Controls.InfoBarSeverity.Warning;
                StartupTitle = "Atoll · Model uyarısı";
                StartupWarningMessage = ex.Message;
                ShowSettingsButton = true;
                ShowStartupWarning = true;
            });
        }
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
