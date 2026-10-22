using System.Collections.Generic;
using System.Windows.Input;
using HyTranslate.Core.Languages;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace HyTranslate.App.Controls;

public sealed partial class LanguageSelectorControl : UserControl
{
    public static readonly DependencyProperty SourceLanguagesProperty =
        DependencyProperty.Register(nameof(SourceLanguages), typeof(IReadOnlyList<LanguageItem>), typeof(LanguageSelectorControl), new PropertyMetadata(null));

    public static readonly DependencyProperty TargetLanguagesProperty =
        DependencyProperty.Register(nameof(TargetLanguages), typeof(IReadOnlyList<LanguageItem>), typeof(LanguageSelectorControl), new PropertyMetadata(null));

    public static readonly DependencyProperty SelectedSourceLanguageProperty =
        DependencyProperty.Register(nameof(SelectedSourceLanguage), typeof(LanguageItem), typeof(LanguageSelectorControl), new PropertyMetadata(null));

    public static readonly DependencyProperty SelectedTargetLanguageProperty =
        DependencyProperty.Register(nameof(SelectedTargetLanguage), typeof(LanguageItem), typeof(LanguageSelectorControl), new PropertyMetadata(null));

    public static readonly DependencyProperty SwapCommandProperty =
        DependencyProperty.Register(nameof(SwapCommand), typeof(ICommand), typeof(LanguageSelectorControl), new PropertyMetadata(null));

    public static readonly DependencyProperty CanSwapProperty =
        DependencyProperty.Register(nameof(CanSwap), typeof(bool), typeof(LanguageSelectorControl), new PropertyMetadata(true));

    public IReadOnlyList<LanguageItem> SourceLanguages
    {
        get => (IReadOnlyList<LanguageItem>)GetValue(SourceLanguagesProperty);
        set => SetValue(SourceLanguagesProperty, value);
    }

    public IReadOnlyList<LanguageItem> TargetLanguages
    {
        get => (IReadOnlyList<LanguageItem>)GetValue(TargetLanguagesProperty);
        set => SetValue(TargetLanguagesProperty, value);
    }

    public LanguageItem SelectedSourceLanguage
    {
        get => (LanguageItem)GetValue(SelectedSourceLanguageProperty);
        set => SetValue(SelectedSourceLanguageProperty, value);
    }

    public LanguageItem SelectedTargetLanguage
    {
        get => (LanguageItem)GetValue(SelectedTargetLanguageProperty);
        set => SetValue(SelectedTargetLanguageProperty, value);
    }

    public ICommand SwapCommand
    {
        get => (ICommand)GetValue(SwapCommandProperty);
        set => SetValue(SwapCommandProperty, value);
    }

    public bool CanSwap
    {
        get => (bool)GetValue(CanSwapProperty);
        set => SetValue(CanSwapProperty, value);
    }

    public LanguageSelectorControl()
    {
        this.InitializeComponent();
    }
}
