using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HyTranslate.Core.Languages;
using HyTranslate.Core.Ollama;
using HyTranslate.Core.Translation;
using Microsoft.UI.Dispatching;
using Windows.ApplicationModel.DataTransfer;

namespace HyTranslate.App.ViewModels;

public class TextViewModel : ObservableObject
{
    private readonly ITranslator _translator;
    private readonly DispatcherQueue _dispatcherQueue;
    private CancellationTokenSource? _debounceCts;

    private LanguageItem _selectedSourceLanguage;
    private LanguageItem _selectedTargetLanguage;
    private string _sourceText = string.Empty;
    private string _targetText = string.Empty;
    private bool _isTranslating;
    private string? _errorMessage;
    private bool _hasError;

    public IReadOnlyList<LanguageItem> SourceLanguages { get; }
    public IReadOnlyList<LanguageItem> TargetLanguages { get; }

    public LanguageItem SelectedSourceLanguage
    {
        get => _selectedSourceLanguage;
        set
        {
            if (SetProperty(ref _selectedSourceLanguage, value))
            {
                OnPropertyChanged(nameof(CanSwap));
                ScheduleTranslation();
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
                ScheduleTranslation();
            }
        }
    }

    public bool CanSwap => SelectedSourceLanguage != null &&
                           !string.Equals(SelectedSourceLanguage.Code, Languages.AutoCode, StringComparison.OrdinalIgnoreCase);

    public string SourceText
    {
        get => _sourceText;
        set
        {
            if (SetProperty(ref _sourceText, value))
            {
                OnPropertyChanged(nameof(CharacterCountText));
                ScheduleTranslation();
            }
        }
    }

    public string TargetText
    {
        get => _targetText;
        set => SetProperty(ref _targetText, value);
    }

    public string CharacterCountText => $"{_sourceText.Length} / 5000";

    public bool IsTranslating
    {
        get => _isTranslating;
        set => SetProperty(ref _isTranslating, value);
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

    public IRelayCommand SwapLanguagesCommand { get; }
    public IRelayCommand ClearCommand { get; }
    public IRelayCommand CopyCommand { get; }
    public IAsyncRelayCommand TranslateCommand { get; }

    public TextViewModel(ITranslator translator)
    {
        _translator = translator ?? throw new ArgumentNullException(nameof(translator));
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread() ?? DispatcherQueue.GetForCurrentThread();

        var sourceList = new List<LanguageItem> { Languages.Auto };
        sourceList.AddRange(Languages.SupportedLanguages);
        SourceLanguages = sourceList;

        TargetLanguages = Languages.SupportedLanguages.ToList();

        _selectedSourceLanguage = Languages.Auto;
        _selectedTargetLanguage = Languages.SupportedLanguages.First(l => l.Code == "tr");

        SwapLanguagesCommand = new RelayCommand(SwapLanguages, () => CanSwap);
        ClearCommand = new RelayCommand(ClearSource);
        CopyCommand = new RelayCommand(CopyTarget);
        TranslateCommand = new AsyncRelayCommand(() => PerformTranslationAsync(CancellationToken.None));
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

            // Also swap text if target is present
            if (!string.IsNullOrEmpty(TargetText))
            {
                var prevTargetText = TargetText;
                SourceText = prevTargetText;
            }
            else
            {
                ScheduleTranslation();
            }
        }
    }

    private void ClearSource()
    {
        _debounceCts?.Cancel();
        SourceText = string.Empty;
        TargetText = string.Empty;
        ErrorMessage = null;
        IsTranslating = false;
    }

    private void CopyTarget()
    {
        if (string.IsNullOrEmpty(TargetText)) return;

        var package = new DataPackage();
        package.SetText(TargetText);
        Clipboard.SetContent(package);
    }

    private void ScheduleTranslation()
    {
        _debounceCts?.Cancel();
        _debounceCts = new CancellationTokenSource();
        var token = _debounceCts.Token;

        if (string.IsNullOrWhiteSpace(SourceText))
        {
            TargetText = string.Empty;
            IsTranslating = false;
            ErrorMessage = null;
            return;
        }

        Task.Run(async () =>
        {
            try
            {
                await Task.Delay(600, token).ConfigureAwait(false);
                await PerformTranslationAsync(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Debounced/cancelled
            }
        }, token);
    }

    public async Task PerformTranslationAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(SourceText)) return;

        UpdateUi(() =>
        {
            IsTranslating = true;
            ErrorMessage = null;
            TargetText = string.Empty;
        });

        try
        {
            string srcCode = SelectedSourceLanguage.Code;
            string tgtCode = SelectedTargetLanguage.Code;

            await foreach (var chunk in _translator.TranslateStreamAsync(SourceText, tgtCode, srcCode, ct).ConfigureAwait(false))
            {
                var piece = chunk;
                UpdateUi(() =>
                {
                    TargetText += piece;
                });
            }

            // Post-process final result (trim fences, quotes)
            UpdateUi(() =>
            {
                TargetText = Languages.PostProcessTranslation(TargetText, SourceText);
            });
        }
        catch (OperationCanceledException)
        {
            // Cancelled
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
                ErrorMessage = $"Çeviri hatası: {ex.Message}";
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
