using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using AccountaBall.Core.Models;
using AccountaBall.Core.Services;
using AccountaBall.Core.Util;

namespace AccountaBall.Platform.Services;

/// <see cref="IAiService"/> over a local Ollama daemon (the default provider).
/// Every call pairs a verbatim system prompt from <see cref="AiPrompts"/> with a
/// service-built user message and asks Ollama for JSON (<c>format: "json"</c>,
/// <c>temperature 0</c>) via <c>/api/generate</c>. The structured JSON is then
/// mapped to the Core result types. System prompts are the load-bearing contract;
/// the user-message shaping here is the provider's responsibility.
public sealed class OllamaAiService : IAiService
{
    private readonly HttpClient _http;
    private readonly OllamaConfig _config;

    public OllamaAiService(OllamaConfig? config = null, HttpClient? http = null)
    {
        _config = config ?? OllamaConfig.Resolve();
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
    }

    // MARK: - classifyMulti

    public async Task<MultiTaskResult> ClassifyMultiAsync(
        IReadOnlyList<TaskItem> tasks, string screenText,
        IReadOnlyDictionary<int, IReadOnlyList<string>> allowanceRulesByIndex)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Tasks:");
        for (int i = 0; i < tasks.Count; i++)
        {
            sb.Append(i).Append(". ").Append(tasks[i].Task);
            if (!string.IsNullOrWhiteSpace(tasks[i].Context))
                sb.Append(" — ").Append(tasks[i].Context);
            if (allowanceRulesByIndex.TryGetValue(i, out var rules) && rules.Count > 0)
                sb.Append(" [also counts as on-task: ").Append(string.Join("; ", rules)).Append(']');
            sb.AppendLine();
        }
        sb.AppendLine().AppendLine("Screen text:").Append(screenText);

        var json = await GenerateJsonAsync(AiPrompts.ClassifySystem, sb.ToString());
        // The model emits {"result": "...", "label": "..."}; recombine into the
        // "RESULT | label" line MultiTaskResult.Parse consumes (Parse already
        // falls back to OffTask on anything unexpected).
        if (TryGetString(json, "result", out var result))
        {
            TryGetString(json, "label", out var label);
            return MultiTaskResult.Parse($"{result} | {label}");
        }
        return MultiTaskResult.Parse(json.RootElement.GetRawText());
    }

    // MARK: - evaluateExcuse

    public async Task<ExcuseVerdict> EvaluateExcuseAsync(string excuse, IReadOnlyList<TaskItem> tasks, string screenText)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Tasks:");
        for (int i = 0; i < tasks.Count; i++)
            sb.Append(i).Append(". ").Append(tasks[i].Task)
              .Append(" — ").AppendLine(tasks[i].Context);
        sb.AppendLine().Append("User's explanation: ").AppendLine(excuse);
        sb.AppendLine().AppendLine("Screen text:").Append(screenText);

        var json = await GenerateJsonAsync(AiPrompts.ExcuseSystem, sb.ToString());
        var root = json.RootElement;
        var verdictStr = GetString(root, "verdict");
        var justified = verdictStr.ToUpperInvariant() is var v && !v.Contains("NOT") && v.Contains("JUSTIFIED");
        int? taskIndex = TryGetInt(root, "taskIndex", out var ti) ? ti : null;
        var rule = GetString(root, "rule");
        return new ExcuseVerdict(justified, justified ? taskIndex : null, rule);
    }

    // MARK: - summarizeTask

    public async Task<TaskRecap> SummarizeTaskAsync(
        string title, string context, IReadOnlyList<string> steps, double durationSeconds,
        TaskPreviousRun? previous)
    {
        var sb = new StringBuilder();
        sb.Append("Task: ").AppendLine(title);
        if (!string.IsNullOrWhiteSpace(context)) sb.Append("Context: ").AppendLine(context);
        sb.Append("Duration: ").Append((int)durationSeconds).AppendLine(" seconds");
        if (steps.Count > 0)
        {
            sb.AppendLine("Activity observed:");
            foreach (var s in steps) sb.Append("- ").AppendLine(s);
        }
        if (previous is { } p)
        {
            sb.Append("Previous run: ").Append((int)p.DurationSeconds)
              .Append("s, off-task ").Append(p.OffTaskCount).Append(" times");
            if (p.Steps.Count > 0) sb.Append(", steps: ").Append(string.Join("; ", p.Steps));
            sb.AppendLine();
        }

        var json = await GenerateJsonAsync(AiPrompts.SummarizeSystem, sb.ToString());
        var root = json.RootElement;
        var summary = GetString(root, "summary");
        var stepsOut = GetStringArray(root, "steps");
        var comparison = TryGetString(json, "comparison", out var c) && !string.IsNullOrWhiteSpace(c) ? c : null;
        return new TaskRecap(summary, stepsOut, durationSeconds, comparison);
    }

    // MARK: - matchTask

    public async Task<(string Id, bool Confident)?> MatchTaskAsync(
        string query, IReadOnlyList<(string Id, string Title, string Summary)> candidates)
    {
        if (candidates.Count == 0) return null;
        var sb = new StringBuilder();
        sb.Append("Current focus: ").AppendLine(query);
        sb.AppendLine("Candidates:");
        foreach (var (id, ttitle, summary) in candidates)
            sb.Append("- id: ").Append(id).Append(" | ").Append(ttitle).Append(" | ").AppendLine(summary);

        var json = await GenerateJsonAsync(AiPrompts.MatchSystem, sb.ToString());
        var root = json.RootElement;
        if (!TryGetString(json, "id", out var matchedId) || string.IsNullOrWhiteSpace(matchedId))
            return null;
        var confident = root.TryGetProperty("confident", out var cf)
            && cf.ValueKind == JsonValueKind.True;
        return (matchedId, confident);
    }

    // MARK: - healthCheck

    public async Task<bool> HealthCheckAsync()
    {
        try
        {
            using var resp = await _http.GetAsync($"{_config.Host}/api/tags");
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;   // non-throwing by contract; a failure is "unreachable", never off-task
        }
    }

    // MARK: - summarizeSession

    public async Task<IReadOnlyList<PerTaskComment>> SummarizeSessionAsync(IReadOnlyList<PerTaskSessionInput> perTask)
    {
        if (perTask.Count == 0) return Array.Empty<PerTaskComment>();
        var sb = new StringBuilder();
        foreach (var t in perTask)
        {
            sb.Append("Task: ").AppendLine(t.Title);
            sb.Append("  This session: ").Append((int)t.DurationSeconds).Append("s, off-task ")
              .Append(t.OffTaskCount).AppendLine(" times");
            if (t.LastDurationSeconds is { } last) sb.Append("  Last time: ").Append((int)last).AppendLine("s");
            if (t.AverageSeconds is { } avg) sb.Append("  Average: ").Append((int)avg).AppendLine("s");
            if (!string.IsNullOrWhiteSpace(t.LocalComparison)) sb.Append("  Delta: ").AppendLine(t.LocalComparison);
            if (t.Steps.Count > 0) sb.Append("  Steps: ").AppendLine(string.Join("; ", t.Steps));
        }

        var json = await GenerateJsonAsync(AiPrompts.SessionSystem, sb.ToString());
        var outList = new List<PerTaskComment>();
        if (json.RootElement.TryGetProperty("tasks", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in arr.EnumerateArray())
            {
                var title = GetString(el, "title");
                var comment = GetString(el, "comment");
                var suggestion = TryGetStringFrom(el, "suggestion", out var s) && !string.IsNullOrWhiteSpace(s) ? s : null;
                outList.Add(new PerTaskComment(title, comment, suggestion));
            }
        }
        return outList;
    }

    // MARK: - summarizeFreeBall

    public async Task<FreeBallSummary> SummarizeFreeBallAsync(
        IReadOnlyList<FreeBallTranscriptEntry> transcript, IReadOnlyList<FreeBallPastRecap> pastRecaps)
    {
        var prompt = AiPrompts.BuildFreeBallPrompt(transcript, pastRecaps);
        // ParseFreeBallSummary is deliberately tolerant (slices to the outermost
        // braces), so hand it the raw model response rather than a pre-parsed doc.
        var raw = await GenerateRawResponseAsync(AiPrompts.FreeBallSystem, prompt);
        return AiPrompts.ParseFreeBallSummary(raw);
    }

    // MARK: - HTTP / JSON plumbing

    /// POST to /api/generate with format=json + temperature 0, returning the parsed
    /// `response` payload as a JsonDocument. Throws on transport/HTTP failure so the
    /// caller (the capture loop) can route to the AI-unavailable state.
    private async Task<JsonDocument> GenerateJsonAsync(string system, string prompt)
    {
        var inner = await GenerateRawResponseAsync(system, prompt);
        return JsonDocument.Parse(string.IsNullOrWhiteSpace(inner) ? "{}" : inner);
    }

    /// POST to /api/generate and return the model's raw `response` string (the JSON
    /// body it produced). Throws on transport/HTTP failure so the caller can route to
    /// the AI-unavailable state.
    private async Task<string> GenerateRawResponseAsync(string system, string prompt)
    {
        var body = new
        {
            model = _config.Model,
            system,
            prompt,
            stream = false,
            format = "json",
            options = new { temperature = 0 },
        };

        using var resp = await _http.PostAsJsonAsync($"{_config.Host}/api/generate", body);
        resp.EnsureSuccessStatusCode();
        using var envelope = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        return envelope.RootElement.TryGetProperty("response", out var r) ? r.GetString() ?? "{}" : "{}";
    }

    private static bool TryGetString(JsonDocument doc, string name, out string value)
        => TryGetStringFrom(doc.RootElement, name, out value);

    private static bool TryGetStringFrom(JsonElement el, string name, out string value)
    {
        if (el.ValueKind == JsonValueKind.Object && el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String)
        {
            value = p.GetString() ?? string.Empty;
            return true;
        }
        value = string.Empty;
        return false;
    }

    private static string GetString(JsonElement el, string name)
        => TryGetStringFrom(el, name, out var v) ? v : string.Empty;

    private static bool TryGetInt(JsonElement el, string name, out int value)
    {
        value = 0;
        if (el.ValueKind != JsonValueKind.Object || !el.TryGetProperty(name, out var p)) return false;
        if (p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out value)) return true;
        if (p.ValueKind == JsonValueKind.String && int.TryParse(p.GetString(), out value)) return true;
        return false;
    }

    private static IReadOnlyList<string> GetStringArray(JsonElement el, string name)
    {
        var list = new List<string>();
        if (el.ValueKind == JsonValueKind.Object && el.TryGetProperty(name, out var arr) && arr.ValueKind == JsonValueKind.Array)
            foreach (var item in arr.EnumerateArray())
                if (item.ValueKind == JsonValueKind.String && item.GetString() is { } s)
                    list.Add(s);
        return list;
    }
}
