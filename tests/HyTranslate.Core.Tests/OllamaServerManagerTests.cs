using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HyTranslate.Core.Ollama;
using HyTranslate.Core.Settings;
using Xunit;

namespace HyTranslate.Core.Tests;

public class OllamaServerManagerTests
{
    [Fact]
    public void FindOllamaExe_ConfiguredPathExists_ReturnsConfiguredPath()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"fake_ollama_{Guid.NewGuid():N}.exe");
        try
        {
            File.WriteAllText(tempFile, "dummy");

            var result = OllamaServerManager.FindOllamaExeStatic(tempFile);

            Assert.NotNull(result);
            Assert.Equal(Path.GetFullPath(tempFile), Path.GetFullPath(result));
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public void FindOllamaExe_ConfiguredPathDoesNotExist_FallsBackToAutoDetection()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"fake_bin_{Guid.NewGuid():N}");
        var emptyDir = Path.Combine(Path.GetTempPath(), $"empty_dir_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        Directory.CreateDirectory(emptyDir);

        var fakeExeOnPath = Path.Combine(tempDir, "ollama.exe");
        File.WriteAllText(fakeExeOnPath, "dummy");

        try
        {
            var nonExistentConfigured = Path.Combine(Path.GetTempPath(), $"non_existent_{Guid.NewGuid():N}.exe");

            // Isolated fallback test: localAppData and progFiles are empty, so it must find fakeExeOnPath from PATH
            var resultIsolated = OllamaServerManager.FindOllamaExeStatic(
                nonExistentConfigured,
                localAppDataOverride: emptyDir,
                progFilesOverride: emptyDir,
                pathOverride: tempDir);

            Assert.NotNull(resultIsolated);
            Assert.Equal(Path.GetFullPath(fakeExeOnPath), Path.GetFullPath(resultIsolated));

            // When no candidate exists anywhere, returns null
            var resultNull = OllamaServerManager.FindOllamaExeStatic(
                nonExistentConfigured,
                localAppDataOverride: emptyDir,
                progFilesOverride: emptyDir,
                pathOverride: emptyDir);

            Assert.Null(resultNull);

            // Live auto-detection test: non-existent configured path does not return the non-existent path
            var liveResult = OllamaServerManager.FindOllamaExeStatic(nonExistentConfigured);
            if (liveResult != null)
            {
                Assert.NotEqual(nonExistentConfigured, liveResult);
                Assert.True(File.Exists(liveResult));
            }
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
            try { Directory.Delete(emptyDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task StartAsync_NonExistentFolder_ReturnsErrorWithoutStartingServer()
    {
        using var manager = new OllamaServerManager();
        var nonExistentPath = Path.Combine(Path.GetTempPath(), $"missing_models_{Guid.NewGuid():N}");

        var result = await manager.StartAsync(nonExistentPath, 11435, null, CancellationToken.None);

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        Assert.Contains("bulunamadı", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.False(manager.IsRunning);
    }

    [Fact]
    public async Task AppSettings_OldJsonLoadsWithDefaultValues()
    {
        // JSON simulating an older version without ModelsPath, OllamaExePath, or ManagedPort
        var oldJson = """
        {
          "OllamaUrl": "http://localhost:11434",
          "ModelName": "maternion/hy-mt2:1.8b",
          "Theme": "Dark",
          "TranslateSheetNames": true,
          "DefaultSourceLang": "en",
          "DefaultTargetLang": "tr"
        }
        """;

        var tempSettingsFile = Path.Combine(Path.GetTempPath(), $"settings_{Guid.NewGuid():N}.json");
        try
        {
            await File.WriteAllTextAsync(tempSettingsFile, oldJson);

            var settingsManager = new SettingsManager(tempSettingsFile);
            var settings = await settingsManager.LoadSettingsAsync();

            Assert.Equal("http://localhost:11434", settings.OllamaUrl);
            Assert.Equal("maternion/hy-mt2:1.8b", settings.ModelName);
            Assert.Equal("Dark", settings.Theme);
            Assert.True(settings.TranslateSheetNames);
            Assert.Equal("en", settings.DefaultSourceLang);
            Assert.Equal("tr", settings.DefaultTargetLang);

            // New fields default values
            Assert.Equal(string.Empty, settings.ModelsPath);
            Assert.Equal(string.Empty, settings.OllamaExePath);
            Assert.Equal(11435, settings.ManagedPort);
        }
        finally
        {
            if (File.Exists(tempSettingsFile))
            {
                File.Delete(tempSettingsFile);
            }
        }
    }

    [Fact]
    public async Task OllamaClient_Reconfigure_RequestsGoToNewBaseUrl()
    {
        var recordedRequests = new List<HttpRequestMessage>();

        var testHandler = new TestHttpMessageHandler((req) =>
        {
            recordedRequests.Add(req);

            var uri = req.RequestUri?.ToString() ?? string.Empty;
            if (uri.EndsWith("/api/version", StringComparison.OrdinalIgnoreCase))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"version\":\"0.1.0\"}")
                };
            }
            if (uri.EndsWith("/api/tags", StringComparison.OrdinalIgnoreCase))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"models\":[{\"name\":\"maternion/hy-mt2:1.8b\"},{\"name\":\"custom-model:latest\"}]}")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}")
            };
        });

        using var httpClient = new HttpClient(testHandler);
        using var client = new OllamaClient("http://localhost:11434", "initial-model", httpClient);

        // 1. Initial request to default URL
        var isAliveInitial = await client.IsAliveAsync();
        Assert.True(isAliveInitial);
        Assert.Single(recordedRequests);
        Assert.Equal("http://localhost:11434/api/version", recordedRequests[0].RequestUri?.ToString());

        // 2. Reconfigure with managed server URL and new model
        client.Reconfigure("http://127.0.0.1:11435", "custom-model");
        Assert.Equal("http://127.0.0.1:11435", client.BaseUrl);
        Assert.Equal("custom-model", client.ModelName);

        // 3. Request after Reconfigure must go to new base URL
        var isAliveReconfigured = await client.IsAliveAsync();
        Assert.True(isAliveReconfigured);
        Assert.Equal(2, recordedRequests.Count);
        Assert.Equal("http://127.0.0.1:11435/api/version", recordedRequests[1].RequestUri?.ToString());

        // 4. List models goes to new base URL
        var models = await client.ListModelsAsync();
        Assert.Equal(2, models.Count);
        Assert.Contains("custom-model:latest", models);
        Assert.Equal(3, recordedRequests.Count);
        Assert.Equal("http://127.0.0.1:11435/api/tags", recordedRequests[2].RequestUri?.ToString());
    }

    private class TestHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public TestHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(_responder(request));
        }
    }
}
