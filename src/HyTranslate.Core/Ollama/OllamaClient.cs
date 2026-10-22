using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace HyTranslate.Core.Ollama;

public class OllamaClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly bool _disposeClient;
    private readonly object _lock = new();

    private string _baseUrl;
    private string _modelName;

    public string BaseUrl
    {
        get
        {
            lock (_lock) return _baseUrl;
        }
    }

    public string ModelName
    {
        get
        {
            lock (_lock) return _modelName;
        }
    }

    public OllamaClient(string? baseUrl = null, string? modelName = null, HttpClient? httpClient = null)
    {
        _baseUrl = string.IsNullOrWhiteSpace(baseUrl) ? "http://localhost:11434" : baseUrl.TrimEnd('/');
        _modelName = string.IsNullOrWhiteSpace(modelName) ? "maternion/hy-mt2:1.8b" : modelName.Trim();

        if (httpClient != null)
        {
            _httpClient = httpClient;
            _disposeClient = false;
        }
        else
        {
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromMinutes(10)
            };
            _disposeClient = true;
        }
    }

    public void Reconfigure(string baseUrl, string modelName)
    {
        lock (_lock)
        {
            _baseUrl = string.IsNullOrWhiteSpace(baseUrl) ? "http://localhost:11434" : baseUrl.TrimEnd('/');
            _modelName = string.IsNullOrWhiteSpace(modelName) ? "maternion/hy-mt2:1.8b" : modelName.Trim();
        }
    }

    public async Task<bool> IsAliveAsync(CancellationToken ct = default)
    {
        try
        {
            string currentBaseUrl;
            lock (_lock) currentBaseUrl = _baseUrl;

            var uri = new Uri($"{currentBaseUrl}/api/version");
            using var response = await _httpClient.GetAsync(uri, ct).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    public async Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct = default)
    {
        try
        {
            string currentBaseUrl;
            lock (_lock) currentBaseUrl = _baseUrl;

            var uri = new Uri($"{currentBaseUrl}/api/tags");
            using var response = await _httpClient.GetAsync(uri, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return Array.Empty<string>();

            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);

            var list = new List<string>();
            if (doc.RootElement.TryGetProperty("models", out var modelsElement) &&
                modelsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var m in modelsElement.EnumerateArray())
                {
                    if (m.TryGetProperty("name", out var nameProp))
                    {
                        var name = nameProp.GetString();
                        if (!string.IsNullOrWhiteSpace(name))
                        {
                            list.Add(name);
                        }
                    }
                }
            }

            return list;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    public async Task<bool> HasModelAsync(string? model = null, CancellationToken ct = default)
    {
        string targetModel;
        string currentBaseUrl;
        lock (_lock)
        {
            targetModel = string.IsNullOrWhiteSpace(model) ? _modelName : model.Trim();
            currentBaseUrl = _baseUrl;
        }

        try
        {
            var uri = new Uri($"{currentBaseUrl}/api/tags");
            using var response = await _httpClient.GetAsync(uri, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return false;

            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("models", out var modelsElement) &&
                modelsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var m in modelsElement.EnumerateArray())
                {
                    if (m.TryGetProperty("name", out var nameProp))
                    {
                        var name = nameProp.GetString();
                        if (name == null) continue;
                        if (string.Equals(name, targetModel, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(name, $"{targetModel}:latest", StringComparison.OrdinalIgnoreCase) ||
                            (targetModel.EndsWith(":latest", StringComparison.OrdinalIgnoreCase) &&
                             string.Equals(name, targetModel[..^7], StringComparison.OrdinalIgnoreCase)))
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new OllamaException(OllamaException.DefaultErrorMessage, ex);
        }
    }

    public async IAsyncEnumerable<PullProgress> PullModelAsync(
        string? model = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        string targetModel;
        string currentBaseUrl;
        lock (_lock)
        {
            targetModel = string.IsNullOrWhiteSpace(model) ? _modelName : model.Trim();
            currentBaseUrl = _baseUrl;
        }

        var uri = new Uri($"{currentBaseUrl}/api/pull");

        var payload = JsonSerializer.Serialize(new
        {
            name = targetModel,
            stream = true
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };

        HttpResponseMessage? response = null;
        try
        {
            response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            response?.Dispose();
            throw;
        }
        catch (Exception ex)
        {
            response?.Dispose();
            throw new OllamaException(OllamaException.DefaultErrorMessage, ex);
        }

        using (response)
        using (var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
        using (var reader = new StreamReader(stream))
        {
            while (!reader.EndOfStream)
            {
                ct.ThrowIfCancellationRequested();
                var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(line)) continue;

                PullProgress? progress = null;
                try
                {
                    progress = JsonSerializer.Deserialize<PullProgress>(line);
                }
                catch
                {
                    // Ignore parse error on non-standard line
                }

                if (progress != null)
                {
                    yield return progress;
                }
            }
        }
    }

    public async IAsyncEnumerable<string> ChatStreamAsync(
        string prompt,
        OllamaOptions? options = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        options ??= new OllamaOptions();
        string currentModel;
        string currentBaseUrl;
        lock (_lock)
        {
            currentModel = _modelName;
            currentBaseUrl = _baseUrl;
        }

        var uri = new Uri($"{currentBaseUrl}/api/chat");

        var requestBody = new
        {
            model = currentModel,
            messages = new[]
            {
                new { role = "user", content = prompt }
            },
            stream = true,
            keep_alive = "30m",
            options = new
            {
                temperature = options.Temperature,
                top_p = options.TopP,
                top_k = options.TopK,
                repeat_penalty = options.RepeatPenalty,
                num_predict = options.NumPredict
            }
        };

        var jsonPayload = JsonSerializer.Serialize(requestBody);
        using var request = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json")
        };

        HttpResponseMessage? response = null;
        try
        {
            response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            response?.Dispose();
            throw;
        }
        catch (Exception ex)
        {
            response?.Dispose();
            throw new OllamaException(OllamaException.DefaultErrorMessage, ex);
        }

        using (response)
        using (var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
        using (var reader = new StreamReader(stream))
        {
            while (!reader.EndOfStream)
            {
                ct.ThrowIfCancellationRequested();
                var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(line)) continue;

                string? chunk = null;
                try
                {
                    using var doc = JsonDocument.Parse(line);
                    if (doc.RootElement.TryGetProperty("message", out var msgElement) &&
                        msgElement.TryGetProperty("content", out var contentElement))
                    {
                        chunk = contentElement.GetString();
                    }
                }
                catch
                {
                    // Ignore malformed line
                }

                if (!string.IsNullOrEmpty(chunk))
                {
                    yield return chunk;
                }
            }
        }
    }

    public async Task<string> ChatAsync(
        string prompt,
        OllamaOptions? options = null,
        CancellationToken ct = default)
    {
        var sb = new StringBuilder();
        await foreach (var chunk in ChatStreamAsync(prompt, options, ct).ConfigureAwait(false))
        {
            sb.Append(chunk);
        }
        return sb.ToString();
    }

    public void Dispose()
    {
        if (_disposeClient)
        {
            _httpClient.Dispose();
        }
    }
}
