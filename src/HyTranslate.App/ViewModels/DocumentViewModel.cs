using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HyTranslate.Core.Documents;
using HyTranslate.Core.Languages;
using HyTranslate.Core.Ollama;
using HyTranslate.Core.Settings;
using HyTranslate.Core.Translation;
using Microsoft.UI.Dispatching;

namespace HyTranslate.App.ViewModels;

public class DocumentViewModel : ObservableObject
{
    private readonly DocumentTranslatorFactory _translatorFactory;
    private readonly SettingsManager? _settingsManager;
    private readonly AppSettings _appSettings;
    private readonly DispatcherQueue _dispatcherQueue;
    private CancellationTokenSource? _translationCts;

    private bool _outputPathChosenByUser;

    private string? _selectedFilePath;
    private string? _selectedFileName;
    private string? _selectedFileSize;
    private string? _selectedFileType;
    private bool _hasSelectedFile;

    private LanguageItem _selectedSourceLanguage;
    private LanguageItem _selectedTargetLanguage;

    private bool _isTranslating;
    private double _progressValue;
    private string _progressText = string.Empty;

    private string? _outputFilePath;
    private string? _successMessage;
    private bool _hasSuccess;
    private string? _errorMessage;
    private bool _hasError;

    public IReadOnlyList<LanguageItem> SourceLanguages { get; }
    public IReadOnlyList<LanguageItem> TargetLanguages { get; }

    public ObservableCollection<string> Warnings { get; } = new();

    public string? SelectedFilePath
    {
        get => _selectedFilePath;
        set
        {
            if (SetProperty(ref _selectedFilePath, value))
            {
                HasSelectedFile = !string.IsNullOrEmpty(value);
                if (!string.IsNullOrEmpty(value) && File.Exists(value))
                {
                    var fileInfo = new FileInfo(value);
                    SelectedFileName = fileInfo.Name;
                    SelectedFileSize = FormatFileSize(fileInfo.Length);
                    SelectedFileType = fileInfo.Extension.ToUpperInvariant().TrimStart('.') + " Dosyası";
                    _outputPathChosenByUser = false;
                    UpdateDefaultOutputPath();
                }
                else
                {
                    SelectedFileName = null;
                    SelectedFileSize = null;
                    SelectedFileType = null;
                    _outputPathChosenByUser = false;
                    OutputFilePath = null;
                }
            }
        }
    }

    public string? SelectedFileName
    {
        get => _selectedFileName;
        set => SetProperty(ref _selectedFileName, value);
    }

    public string? SelectedFileSize
    {
        get => _selectedFileSize;
        set => SetProperty(ref _selectedFileSize, value);
    }

    public string? SelectedFileType
    {
        get => _selectedFileType;
        set => SetProperty(ref _selectedFileType, value);
    }

    public bool HasSelectedFile
    {
        get => _hasSelectedFile;
        set => SetProperty(ref _hasSelectedFile, value);
    }

    public LanguageItem SelectedSourceLanguage
    {
        get => _selectedSourceLanguage;
        set
        {
            if (SetProperty(ref _selectedSourceLanguage, value))
            {
                OnPropertyChanged(nameof(CanSwap));
            }
        }
    }

    public LanguageItem SelectedTargetLanguage
    {
        get => _selectedTargetLanguage;
        set
        {
            if (SetProperty(ref _selectedTargetLanguage, value))
            {
                if (!_outputPathChosenByUser && !string.IsNullOrEmpty(SelectedFilePath))
                {
                    UpdateDefaultOutputPath();
                }
            }
        }
    }

    public bool CanSwap => SelectedSourceLanguage != null &&
                           !string.Equals(SelectedSourceLanguage.Code, Languages.AutoCode, StringComparison.OrdinalIgnoreCase);

    public bool IsTranslating
    {
        get => _isTranslating;
        set
        {
            if (SetProperty(ref _isTranslating, value))
            {
                OnPropertyChanged(nameof(CanCancel));
            }
        }
    }

    public bool CanCancel => _isTranslating;

    public double ProgressValue
    {
        get => _progressValue;
        set => SetProperty(ref _progressValue, value);
    }

    public string ProgressText
    {
        get => _progressText;
        set => SetProperty(ref _progressText, value);
    }

    public string? OutputFilePath
    {
        get => _outputFilePath;
        set => SetProperty(ref _outputFilePath, value);
    }

    public string? SuccessMessage
    {
        get => _successMessage;
        set
        {
            if (SetProperty(ref _successMessage, value))
            {
                HasSuccess = !string.IsNullOrEmpty(value);
            }
        }
    }

    public bool HasSuccess
    {
        get => _hasSuccess;
        set => SetProperty(ref _hasSuccess, value);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        set
        {
            if (SetProperty(ref _errorMessage, value))
            {
                HasError = !string.IsNullOrEmpty(value);
            }
        }
    }

    public bool HasError
    {
        get => _hasError;
        set => SetProperty(ref _hasError, value);
    }

    public bool HasWarnings => Warnings.Count > 0;

    public IRelayCommand SwapLanguagesCommand { get; }
    public IAsyncRelayCommand TranslateDocumentCommand { get; }
    public IRelayCommand CancelCommand { get; }
    public IRelayCommand OpenFileCommand { get; }
    public IRelayCommand OpenFolderCommand { get; }

    public DocumentViewModel(
        DocumentTranslatorFactory translatorFactory,
        SettingsManager? settingsManager = null,
        AppSettings? appSettings = null)
    {
        _translatorFactory = translatorFactory ?? throw new ArgumentNullException(nameof(translatorFactory));
        _settingsManager = settingsManager;
        _appSettings = appSettings ?? settingsManager?.LoadSettingsAsync().GetAwaiter().GetResult() ?? new AppSettings();
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();

        var sourceList = new List<LanguageItem> { Languages.Auto };
        sourceList.AddRange(Languages.SupportedLanguages);
        SourceLanguages = sourceList;
        TargetLanguages = Languages.SupportedLanguages.ToList();

        _selectedSourceLanguage = Languages.Auto;
        _selectedTargetLanguage = Languages.SupportedLanguages.First(l => l.Code == "tr");

        SwapLanguagesCommand = new RelayCommand(SwapLanguages, () => CanSwap);
        TranslateDocumentCommand = new AsyncRelayCommand(TranslateDocumentAsync);
        CancelCommand = new RelayCommand(CancelTranslation);
        OpenFileCommand = new RelayCommand(OpenFile);
        OpenFolderCommand = new RelayCommand(OpenFolder);
    }

    public void SetFile(string filePath)
    {
        if (File.Exists(filePath))
        {
            SelectedFilePath = filePath;
            SuccessMessage = null;
            ErrorMessage = null;
            Warnings.Clear();
            OnPropertyChanged(nameof(HasWarnings));
        }
    }

    public void SetOutputPath(string path)
    {
        _outputPathChosenByUser = true;
        OutputFilePath = path;

        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            _appSettings.LastOutputFolder = dir;
            if (_settingsManager != null)
            {
                _ = _settingsManager.SaveSettingsAsync(_appSettings);
            }
        }
    }

    private void UpdateDefaultOutputPath()
    {
        if (string.IsNullOrEmpty(SelectedFilePath) || SelectedTargetLanguage == null)
        {
            OutputFilePath = null;
            return;
        }

        string lastFolder = _appSettings.LastOutputFolder;
        string folder = (!string.IsNullOrWhiteSpace(lastFolder) && Directory.Exists(lastFolder))
            ? lastFolder
            : Path.GetDirectoryName(SelectedFilePath) ?? string.Empty;

        string defaultTargetFile = DocumentTranslatorFactory.GetDefaultOutputPath(SelectedFilePath, SelectedTargetLanguage.Code);
        string fileName = Path.GetFileName(defaultTargetFile);

        OutputFilePath = string.IsNullOrEmpty(folder) ? fileName : Path.Combine(folder, fileName);
    }

    private void SwapLanguages()
    {
        if (!CanSwap) return;
        var prevSource = SelectedSourceLanguage;
        var prevTarget = SelectedTargetLanguage;

        var matchingTarget = TargetLanguages.FirstOrDefault(l => l.Code == prevSource.Code);
        var matchingSource = SourceLanguages.FirstOrDefault(l => l.Code == prevTarget.Code);

        if (matchingTarget != null && matchingSource != null)
        {
            _selectedSourceLanguage = matchingSource;
            _selectedTargetLanguage = matchingTarget;
            OnPropertyChanged(nameof(SelectedSourceLanguage));
            OnPropertyChanged(nameof(SelectedTargetLanguage));
            OnPropertyChanged(nameof(CanSwap));

            if (!_outputPathChosenByUser && !string.IsNullOrEmpty(SelectedFilePath))
            {
                UpdateDefaultOutputPath();
            }
        }
    }

    private async Task TranslateDocumentAsync()
    {
        if (string.IsNullOrEmpty(SelectedFilePath) || !File.Exists(SelectedFilePath))
        {
            ErrorMessage = "Lütfen çevrilecek geçerli bir dosya seçin.";
            return;
        }

        var translator = _translatorFactory.GetTranslator(SelectedFilePath);
        if (translator == null)
        {
            ErrorMessage = "Bu dosya formatı desteklenmiyor (.docx, .pptx, .xlsx, .pdf, .txt, .md).";
            return;
        }

        var targetOutputPath = OutputFilePath;
        if (string.IsNullOrWhiteSpace(targetOutputPath))
        {
            UpdateDefaultOutputPath();
            targetOutputPath = OutputFilePath;
        }

        if (string.IsNullOrWhiteSpace(targetOutputPath))
        {
            ErrorMessage = "Lütfen geçerli bir çıktı yolu belirleyin.";
            return;
        }

        string fullInput = Path.GetFullPath(SelectedFilePath);
        string fullOutput = Path.GetFullPath(targetOutputPath);
        if (string.Equals(fullInput, fullOutput, StringComparison.OrdinalIgnoreCase))
        {
            ErrorMessage = "Çıktı dosyası kaynak dosyayla aynı olamaz.";
            return;
        }

        if (!_outputPathChosenByUser && File.Exists(targetOutputPath))
        {
            targetOutputPath = DocumentTranslatorFactory.MakeUniquePath(targetOutputPath);
            OutputFilePath = targetOutputPath;
        }

        var targetDir = Path.GetDirectoryName(targetOutputPath);
        if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
        {
            Directory.CreateDirectory(targetDir);
        }

        _translationCts = new CancellationTokenSource();
        var ct = _translationCts.Token;

        IsTranslating = true;
        ProgressValue = 0;
        ProgressText = "Başlatılıyor...";
        SuccessMessage = null;
        ErrorMessage = null;
        Warnings.Clear();
        OnPropertyChanged(nameof(HasWarnings));

        var progress = new Progress<TranslationProgress>(p =>
        {
            UpdateUi(() =>
            {
                ProgressValue = p.Percent;
                ProgressText = !string.IsNullOrEmpty(p.CurrentPart)
                    ? $"{p.CurrentPart} (%{p.Percent:F0})"
                    : $"%{p.Percent:F0}";

                if (!string.IsNullOrEmpty(p.Message) && !Warnings.Contains(p.Message))
                {
                    Warnings.Add(p.Message);
                    OnPropertyChanged(nameof(HasWarnings));
                }
            });
        });

        try
        {
            var opt = new TranslateOptions(
                SelectedTargetLanguage.Code,
                SelectedSourceLanguage.Code == Languages.AutoCode ? null : SelectedSourceLanguage.Code,
                _appSettings.TranslateSheetNames,
                _appSettings.OcrEnabled);

            await Task.Run(() => translator.TranslateAsync(SelectedFilePath, targetOutputPath, opt, progress, ct), ct).ConfigureAwait(false);

            UpdateUi(() =>
            {
                ProgressValue = 100;
                SuccessMessage = $"Kaydedildi: {targetOutputPath}";
            });
        }
        catch (OperationCanceledException)
        {
            UpdateUi(() =>
            {
                ErrorMessage = "Çeviri iptal edildi.";
                if (File.Exists(targetOutputPath))
                {
                    try { File.Delete(targetOutputPath); } catch { }
                }
            });
        }
        catch (OllamaException ex)
        {
            UpdateUi(() =>
            {
                ErrorMessage = ex.Message;
            });
        }
        catch (Exception ex)
        {
            UpdateUi(() =>
            {
                ErrorMessage = $"Belge çeviri hatası: {ex.Message}";
            });
        }
        finally
        {
            UpdateUi(() =>
            {
                IsTranslating = false;
            });
        }
    }

    private void CancelTranslation()
    {
        _translationCts?.Cancel();
    }

    private void OpenFile()
    {
        if (string.IsNullOrEmpty(OutputFilePath) || !File.Exists(OutputFilePath)) return;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = OutputFilePath,
                UseShellExecute = true
            });
        }
        catch { }
    }

    private void OpenFolder()
    {
        if (string.IsNullOrEmpty(OutputFilePath) || !File.Exists(OutputFilePath)) return;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{OutputFilePath}\"",
                UseShellExecute = true
            });
        }
        catch { }
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes / (1024.0 * 1024.0):F2} MB";
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
