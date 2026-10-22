using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Channels;
using HyTranslate.Core.Settings;
using LLama;
using LLama.Common;
using LLama.Sampling;
using LLama.Native;

namespace HyTranslate.Core.Translation;

public sealed class EmbeddedModelException(string message, Exception? inner = null) : Exception(message, inner);

public sealed class LlamaSharpTranslator(AppSettings settings) : ITranslator, IDisposable
{
    static LlamaSharpTranslator()
    {
        NativeLibraryConfig.All.WithCuda(false).WithVulkan(false).WithSearchDirectory(AppContext.BaseDirectory);
    }
    public static readonly string[] StopSequences =
    ["<｜hy_User｜>", "<｜hy_Assistant｜>", "<｜hy_begin▁of▁sentence｜>", "<｜hy_end▁of▁sentence｜>"];
    private readonly SemaphoreSlim _gate = new(1, 1);
    private LLamaWeights? _weights;
    private ModelParams? _parameters;
    private string? _loadedPath;
    public string Status { get; private set; } = "Yüklenmedi";
    public event Action? StatusChanged;
    public string? ResolvedPath => ModelPathResolver.Resolve(settings.EmbeddedModelPath, AppContext.BaseDirectory);

    public static string FallbackChatPrompt(string innerPrompt) =>
        $"<｜hy_begin▁of▁sentence｜><｜hy_User｜>{innerPrompt}<｜hy_Assistant｜>";

    private void SetStatus(string status)
    {
        Status = status;
        StatusChanged?.Invoke();
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HyTranslate");
        try
        {
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, "embedded-model.log");
            if (File.Exists(path) && new FileInfo(path).Length > 5 * 1024 * 1024) File.WriteAllText(path, "");
            File.AppendAllText(path, $"{DateTimeOffset.Now:O} {status}{Environment.NewLine}");
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    // Called only while holding the gate. Weights stay loaded across requests; each request has a fresh KV cache.
    private async Task LoadLockedAsync(CancellationToken ct)
    {
        var path = ResolvedPath;
        if (path == null)
            throw new EmbeddedModelException("Model dosyası bulunamadı. Ayarlar'dan ‘Model dosyası seç…’ ile bir GGUF dosyası seçin.");
        if (_weights != null && string.Equals(path, _loadedPath, StringComparison.OrdinalIgnoreCase)) return;
        SetStatus("Yükleniyor…");
        _weights?.Dispose();
        _weights = null;
        _parameters = new ModelParams(path)
        {
            ContextSize = 4096,
            GpuLayerCount = 0,
            Threads = PhysicalCores.Count(),
            BatchThreads = PhysicalCores.Count()
        };
        _weights = await LLamaWeights.LoadFromFileAsync(_parameters, ct).ConfigureAwait(false);
        _loadedPath = path;
        SetStatus($"Hazır · CPU · {_parameters.Threads} fiziksel çekirdek");
    }

    public async Task EnsureLoadedAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try { await Task.Run(() => LoadLockedAsync(ct), ct).ConfigureAwait(false); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            SetStatus($"Hata: {ex.Message}");
            throw new EmbeddedModelException(ex.Message, ex);
        }
        finally { _gate.Release(); }
    }

    public async Task<string> TranslateAsync(string text, string targetLang, string? sourceLang = null, CancellationToken ct = default)
    {
        for (int attempt = 0; attempt < 2; attempt++)
        {
            var result = new StringBuilder();
            await foreach (var token in TranslateStreamAsync(text, targetLang, sourceLang, ct).ConfigureAwait(false)) result.Append(token);
            if (!Languages.Languages.ContainsInstructionEcho(result.ToString(), text))
                return Languages.Languages.PostProcessTranslation(result.ToString(), text);
        }
        return text;
    }

    public async IAsyncEnumerable<string> TranslateStreamAsync(string text, string targetLang, string? sourceLang = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var token = linked.Token;
        var channel = Channel.CreateBounded<string>(64);
        var producer = Task.Run(async () =>
        {
            bool acquired = false;
            try
            {
                await _gate.WaitAsync(token).ConfigureAwait(false);
                acquired = true;
                await LoadLockedAsync(token).ConfigureAwait(false);
                var inner = Languages.Languages.BuildPrompt(text, targetLang, sourceLang);
                string prompt;
                if (_weights!.Metadata.TryGetValue("tokenizer.chat_template", out var templateText) && !string.IsNullOrWhiteSpace(templateText))
                {
                    var template = new LLamaTemplate(_weights) { AddAssistant = true };
                    template.Add("user", inner);
                    prompt = Encoding.UTF8.GetString(template.Apply());
                }
                else prompt = FallbackChatPrompt(inner);

                var executor = new StatelessExecutor(_weights, _parameters!) { ApplyTemplate = false };
                var inference = new InferenceParams
                {
                    MaxTokens = 4096,
                    AntiPrompts = StopSequences,
                    SamplingPipeline = new DefaultSamplingPipeline
                    {
                        Temperature = 0.7f, TopP = 0.6f, TopK = 20, RepeatPenalty = 1.05f
                    }
                };
                // Hold back possible partial stop sequences so they never appear in the UI.
                string pending = "";
                await foreach (var chunk in executor.InferAsync(prompt, inference, token).ConfigureAwait(false))
                {
                    token.ThrowIfCancellationRequested();
                    pending += chunk;
                    int stop = StopSequences.Select(s => pending.IndexOf(s, StringComparison.Ordinal)).Where(i => i >= 0).DefaultIfEmpty(-1).Min();
                    if (stop >= 0)
                    {
                        if (stop > 0) await channel.Writer.WriteAsync(pending[..stop], token);
                        pending = "";
                        break;
                    }
                    int keep = 0;
                    foreach (var sequence in StopSequences)
                        for (int n = 1; n < sequence.Length && n <= pending.Length; n++)
                            if (pending.EndsWith(sequence[..n], StringComparison.Ordinal)) keep = Math.Max(keep, n);
                    if (pending.Length > keep) await channel.Writer.WriteAsync(pending[..(pending.Length - keep)], token);
                    pending = keep == 0 ? "" : pending[^keep..];
                }
                token.ThrowIfCancellationRequested();
                if (pending.Length > 0) await channel.Writer.WriteAsync(pending, token);
                channel.Writer.TryComplete();
            }
            catch (OperationCanceledException ex) { channel.Writer.TryComplete(ex); }
            catch (Exception ex)
            {
                SetStatus($"Hata: {ex.Message}");
                channel.Writer.TryComplete(new EmbeddedModelException($"Gömülü model: {ex.Message}", ex));
            }
            finally { if (acquired) _gate.Release(); }
        }, CancellationToken.None);
        try
        {
            await foreach (var chunk in channel.Reader.ReadAllAsync(ct).ConfigureAwait(false)) yield return chunk;
        }
        finally
        {
            linked.Cancel();
            await producer.ConfigureAwait(false);
        }
    }

    public void Dispose()
    {
        _gate.Wait();
        try { _weights?.Dispose(); _weights = null; }
        finally { _gate.Release(); }
    }
}
