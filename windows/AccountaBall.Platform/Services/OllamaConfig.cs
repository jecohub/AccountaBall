using System;
using System.IO;
using System.Text.Json;

namespace AccountaBall.Platform.Services;

/// Resolves Ollama connection settings: environment variable first, then
/// <c>%LOCALAPPDATA%\AccountaBall\config.json</c>, then the documented defaults
/// (<c>http://localhost:11434</c> / <c>qwen2.5:7b</c>). Mirrors the macOS env-driven
/// provider config (CLAUDE.md "AI Provider").
public sealed record OllamaConfig(string Host, string Model)
{
    public const string DefaultHost = "http://localhost:11434";
    public const string DefaultModel = "qwen2.5:7b";

    public static OllamaConfig Resolve()
    {
        var file = ReadConfigFile();
        var host = FirstNonEmpty(
            Environment.GetEnvironmentVariable("OLLAMA_HOST"),
            file?.OllamaHost,
            DefaultHost);
        var model = FirstNonEmpty(
            Environment.GetEnvironmentVariable("OLLAMA_MODEL"),
            file?.OllamaModel,
            DefaultModel);
        return new OllamaConfig(host!.TrimEnd('/'), model!);
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var v in values)
            if (!string.IsNullOrWhiteSpace(v)) return v;
        return null;
    }

    private static ConfigFile? ReadConfigFile()
    {
        try
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AccountaBall", "config.json");
            if (!File.Exists(path)) return null;
            return JsonSerializer.Deserialize<ConfigFile>(File.ReadAllText(path),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch
        {
            return null;   // a malformed config never blocks startup; defaults apply
        }
    }

    private sealed class ConfigFile
    {
        public string? OllamaHost { get; set; }
        public string? OllamaModel { get; set; }
    }
}
