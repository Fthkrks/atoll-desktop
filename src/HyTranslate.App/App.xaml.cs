using System;
using System.IO;
using HyTranslate.App.ViewModels;
using HyTranslate.Core.Documents;
using HyTranslate.Core.Ocr;
using HyTranslate.Core.Ollama;
using HyTranslate.Core.Settings;
using HyTranslate.Core.Translation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;

namespace HyTranslate.App;

public partial class App : Application
{
    public static Window? CurrentWindow { get; private set; }
    public static IServiceProvider Services { get; private set; } = null!;

    public App()
    {
        this.InitializeComponent();
        Services = ConfigureServices();
    }

    private static IServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        // Settings
        var settingsManager = new SettingsManager();
        services.AddSingleton(settingsManager);

        var appSettings = settingsManager.LoadSettingsAsync().GetAwaiter().GetResult();
        services.AddSingleton(appSettings);

        // Ollama Server Manager
        services.AddSingleton<OllamaServerManager>();

        // Ollama Client
        services.AddSingleton(sp =>
        {
            var baseUrl = !string.IsNullOrWhiteSpace(appSettings.ModelsPath)
                ? $"http://127.0.0.1:{appSettings.ManagedPort}"
                : appSettings.OllamaUrl;
            return new OllamaClient(baseUrl, appSettings.ModelName);
        });

        // Translation
        services.AddSingleton<ITranslator, OllamaTranslator>();
        services.AddSingleton<TranslationService>();

        // OCR
        services.AddSingleton<Func<IPdfPageOcr?>>(sp =>
        {
            var settings = sp.GetRequiredService<AppSettings>();
            return () => new WindowsPdfOcr(settings.OcrLanguage);
        });

        // Document Translators
        services.AddSingleton<IDocumentTranslator, DocxDocumentTranslator>();
        services.AddSingleton<IDocumentTranslator, PptxDocumentTranslator>();
        services.AddSingleton<IDocumentTranslator, XlsxDocumentTranslator>();
        services.AddSingleton<IDocumentTranslator, TxtDocumentTranslator>();
        services.AddSingleton<IDocumentTranslator, MdDocumentTranslator>();
        services.AddSingleton<IDocumentTranslator>(sp => new PdfDocumentTranslator(sp.GetRequiredService<TranslationService>(), sp.GetRequiredService<Func<IPdfPageOcr?>>()));
        services.AddSingleton<DocumentTranslatorFactory>();

        // ViewModels
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<TextViewModel>();
        services.AddSingleton<DocumentViewModel>();
        services.AddSingleton<SettingsViewModel>();

        return services.BuildServiceProvider();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        CurrentWindow = new MainWindow();

        // Apply saved theme
        var appSettings = Services.GetRequiredService<AppSettings>();
        if (CurrentWindow.Content is FrameworkElement rootElement)
        {
            rootElement.RequestedTheme = appSettings.Theme switch
            {
                "Light" => ElementTheme.Light,
                "Dark" => ElementTheme.Dark,
                _ => ElementTheme.Default
            };
        }

        CurrentWindow.Activate();
    }
}
