using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AccountaBall.Core.Engine;
using AccountaBall.Core.Models;
using AccountaBall.Core.Services;
using AccountaBall.Core.Storage;

namespace AccountaBall.Core.Tests;

/// Mutable clock so a `Now` closure reads an updatable time (mirrors the Swift
/// tests' `Box`). Base time is a fixed epoch for clean arithmetic.
internal sealed class MutableClock
{
    public DateTimeOffset Now = DateTimeOffset.UnixEpoch;
    public Func<DateTimeOffset> Func => () => Now;
}

/// classifyMulti always on-task, health always passes. Off-task signals are
/// injected directly via ProcessResult in the suites that use it.
internal class AlwaysOnTaskAI : IAiService
{
    public Task<MultiTaskResult> ClassifyMultiAsync(IReadOnlyList<TaskItem> tasks, string screenText,
        IReadOnlyDictionary<int, IReadOnlyList<string>> allowanceRulesByIndex)
        => Task.FromResult<MultiTaskResult>(new MultiTaskResult.OnTask(0, ""));
    public virtual Task<ExcuseVerdict> EvaluateExcuseAsync(string excuse, IReadOnlyList<TaskItem> tasks, string screenText)
        => Task.FromResult(new ExcuseVerdict(false, null, ""));
    public virtual Task<TaskRecap> SummarizeTaskAsync(string title, string context, IReadOnlyList<string> steps,
        double durationSeconds, TaskPreviousRun? previous)
        => Task.FromResult(new TaskRecap("", Array.Empty<string>(), durationSeconds, null));
    public Task<(string Id, bool Confident)?> MatchTaskAsync(string query,
        IReadOnlyList<(string Id, string Title, string Summary)> candidates)
        => Task.FromResult<(string, bool)?>(null);
    public virtual Task<bool> HealthCheckAsync() => Task.FromResult(true);
    public Task<IReadOnlyList<PerTaskComment>> SummarizeSessionAsync(IReadOnlyList<PerTaskSessionInput> perTask)
        => Task.FromResult<IReadOnlyList<PerTaskComment>>(Array.Empty<PerTaskComment>());
    public virtual Task<FreeBallSummary> SummarizeFreeBallAsync(
        IReadOnlyList<FreeBallTranscriptEntry> transcript, IReadOnlyList<FreeBallPastRecap> pastRecaps)
        => Task.FromResult(new FreeBallSummary("", Array.Empty<CategorySpan>(), "",
            Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>()));
}

/// Returns a fixed excuse verdict (classifyMulti off-task).
internal sealed class VerdictFakeAI : AlwaysOnTaskAI
{
    private readonly ExcuseVerdict _verdict;
    public VerdictFakeAI(ExcuseVerdict verdict) => _verdict = verdict;
    public override Task<ExcuseVerdict> EvaluateExcuseAsync(string excuse, IReadOnlyList<TaskItem> tasks, string screenText)
        => Task.FromResult(_verdict);
}

/// Unhealthy until Recover() is called (models a provider going down then back).
internal sealed class FlakyAI : AlwaysOnTaskAI
{
    private bool _healthy;
    public void Recover() => _healthy = true;
    public override Task<bool> HealthCheckAsync() => Task.FromResult(_healthy);
}

internal static class EngineTest
{
    /// Engine with no store (decision-logic suites that inject via ProcessResult).
    public static (AppState State, AccountabilityEngine Engine) MakeNoStore(params string[] titles)
    {
        var state = new AppState
        {
            Tasks = titles.Select(t => new TaskItem { Task = t, Context = "for client" }).ToList(),
        };
        state.StartSession();
        var engine = new AccountabilityEngine(state, new AlwaysOnTaskAI(), new NullNotifier());
        return (state, engine);
    }

    /// Engine with an in-memory store and an open session.
    public static (AppState State, AccountabilityEngine Engine, InMemoryStore Store) MakeWithSession(
        IAiService? ai = null, params string[] titles)
    {
        var store = new InMemoryStore();
        var state = new AppState
        {
            Tasks = titles.Select(t => new TaskItem { Task = t, Context = "" }).ToList(),
        };
        state.StartSession();
        var engine = new AccountabilityEngine(state, ai ?? new AlwaysOnTaskAI(), new NullNotifier()) { Store = store };
        engine.BeginSession(state.Tasks);
        return (state, engine, store);
    }

    /// Like MakeWithSession, but injects the clock BEFORE BeginSession so the
    /// settle window is armed against the mutable clock (deterministic timing).
    public static (AppState State, AccountabilityEngine Engine, InMemoryStore Store, MutableClock Clock) MakeWithClock(
        IAiService? ai = null, params string[] titles)
    {
        var store = new InMemoryStore();
        var state = new AppState
        {
            Tasks = titles.Select(t => new TaskItem { Task = t, Context = "" }).ToList(),
        };
        state.StartSession();
        var engine = new AccountabilityEngine(state, ai ?? new AlwaysOnTaskAI(), new NullNotifier()) { Store = store };
        var clock = new MutableClock();
        engine.Now = clock.Func;
        engine.BeginSession(state.Tasks);
        return (state, engine, store, clock);
    }
}
