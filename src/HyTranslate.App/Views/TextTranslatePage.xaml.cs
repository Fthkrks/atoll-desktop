using System.Threading;
using HyTranslate.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace HyTranslate.App.Views;

public sealed partial class TextTranslatePage : Page
{
    public TextViewModel ViewModel { get; }

    public TextTranslatePage()
    {
        ViewModel = App.Services.GetRequiredService<TextViewModel>();
        this.InitializeComponent();
    }

    private async void SourceTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        var ctrlPressed = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

        if (e.Key == VirtualKey.Enter && ctrlPressed)
        {
            e.Handled = true;
            await ViewModel.PerformTranslationAsync(CancellationToken.None);
        }
    }

    private void PanesGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.NewSize.Width >= 720)
        {
            // Side-by-side
            LeftCol.Width = new GridLength(1, GridUnitType.Star);
            RightCol.Width = new GridLength(1, GridUnitType.Star);
            TopRow.Height = new GridLength(1, GridUnitType.Star);
            BottomRow.Height = new GridLength(0);
            Grid.SetRow(LeftCard, 0);
            Grid.SetColumn(LeftCard, 0);
            Grid.SetRow(RightCard, 0);
            Grid.SetColumn(RightCard, 1);
        }
        else
        {
            // Stacked vertically
            LeftCol.Width = new GridLength(1, GridUnitType.Star);
            RightCol.Width = new GridLength(0);
            TopRow.Height = new GridLength(1, GridUnitType.Star);
            BottomRow.Height = new GridLength(1, GridUnitType.Star);
            Grid.SetRow(LeftCard, 0);
            Grid.SetColumn(LeftCard, 0);
            Grid.SetRow(RightCard, 1);
            Grid.SetColumn(RightCard, 0);
        }
    }
}
