using System;
using HyTranslate.App.ViewModels;
using HyTranslate.App.Views;
using HyTranslate.Core.Ollama;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace HyTranslate.App;

public sealed partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; }

    public MainWindow()
    {
        ViewModel = App.Services.GetRequiredService<MainViewModel>();
        this.InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon(System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Atoll.ico"));

        this.Activated += MainWindow_Activated;
        this.Closed += MainWindow_Closed;
    }

    private async void MainWindow_Activated(object sender, WindowActivatedEventArgs args)
    {
        this.Activated -= MainWindow_Activated;
        await ViewModel.CheckStartupStatusAsync();
    }

    private async void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        var serverManager = App.Services.GetService<OllamaServerManager>();
        if (serverManager != null)
        {
            await serverManager.StopAsync();
        }
        App.Services.GetService<HyTranslate.Core.Translation.LlamaSharpTranslator>()?.Dispose();
    }

    private void NavView_Loaded(object sender, RoutedEventArgs e)
    {
        // Default to Metin (Text translation page)
        if (NavView.MenuItems.Count > 0 && NavView.MenuItems[0] is NavigationViewItem firstItem)
        {
            NavView.SelectedItem = firstItem;
            NavigateTo("text");
        }
    }

    private void NavView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (args.InvokedItemContainer is NavigationViewItem item && item.Tag is string tag)
        {
            NavigateTo(tag);
        }
    }

    private void NavigateTo(string tag)
    {
        Type pageType = tag switch
        {
            "text" => typeof(TextTranslatePage),
            "document" => typeof(DocumentTranslatePage),
            "settings" => typeof(SettingsPage),
            _ => typeof(TextTranslatePage)
        };

        if (ContentFrame.CurrentSourcePageType != pageType)
        {
            ContentFrame.Navigate(pageType);
        }
    }

    private void OpenSettingsFromInfoBar_Click(object sender, RoutedEventArgs e)
    {
        if (NavView.FooterMenuItems.Count > 0 && NavView.FooterMenuItems[0] is NavigationViewItem settingsItem)
        {
            NavView.SelectedItem = settingsItem;
            NavigateTo("settings");
        }
    }
}
