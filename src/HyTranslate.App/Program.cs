using HyTranslate.Core.Settings;
using HyTranslate.Core.Translation;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace HyTranslate.App;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Contains("--selftest", StringComparer.OrdinalIgnoreCase))
            return SelfTestAsync(args).GetAwaiter().GetResult();

        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(p =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            _ = new App();
        });
        return 0;
    }

    private static async Task<int> SelfTestAsync(string[] args)
    {
        string? Argument(string name)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }
        try
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            // Deliberately ignore saved backend/model settings: test the portable bundle in isolation.
            var settings = new AppSettings { EmbeddedModelPath = Argument("--model") ?? "" };
            using var translator = new LlamaSharpTranslator(settings);
            translator.StatusChanged += () => Console.Error.WriteLine(translator.Status);
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            await translator.EnsureLoadedAsync(timeout.Token);
            var output = await translator.TranslateAsync(Argument("--text") ?? "Hello, how are you?", "tr", ct: timeout.Token);
            if (string.IsNullOrWhiteSpace(output)) throw new InvalidOperationException("Model returned no translation.");
            Console.WriteLine(output);
            if (args.Contains("--check-cancellation"))
            {
                using var cancel = new CancellationTokenSource();
                try
                {
                    await foreach (var chunk in translator.TranslateStreamAsync("Please describe the weather today.", "tr", ct: cancel.Token))
                        cancel.Cancel();
                    throw new InvalidOperationException("Cancellation did not interrupt generation.");
                }
                catch (OperationCanceledException) { Console.Error.WriteLine("Cancellation: OK"); }
                var retry = await translator.TranslateAsync("Hello", "tr", ct: timeout.Token);
                if (string.IsNullOrWhiteSpace(retry)) throw new InvalidOperationException("Translation after cancellation failed.");
                Console.Error.WriteLine($"After cancellation: {retry}");
            }
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }
}
