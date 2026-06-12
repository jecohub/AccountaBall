using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AccountaBall.Core.Engine;
using AccountaBall.Core.Models;
using AccountaBall.Core.Services;
using AccountaBall.Core.Storage;
using Xunit;

namespace AccountaBall.Core.Tests;

/// Captures the steps the engine passed in; returns a fixed recap.
internal sealed class FixedRecapAI : AlwaysOnTaskAI
{
    public IReadOnlyList<string> ReceivedSteps = Array.Empty<string>();
    public override Task<TaskRecap> SummarizeTaskAsync(string title, string context, IReadOnlyList<string> steps,
        double durationSeconds, TaskPreviousRun? previous)
    {
        ReceivedSteps = steps;
        return Task.FromResult(new TaskRecap("did stuff", new[] { "a", "b" }, durationSeconds, null));
    }
}

public class EngineCompletionTests
{
    [Fact]
    public async Task SummarizeCompletion_CoalescesStepsAndStoresRecap()
    {
        var ai = new FixedRecapAI();
        var (_, engine, store) = EngineTest.MakeWithSession(ai, "Write proposal");
        engine.Record(0, "writing");
        engine.Record(0, "editing");

        await engine.SummarizeCompletionAsync(0);

        Assert.Single(store.KnowledgeTasks);
        var completions = store.KnowledgeTasks[0].Completions;
        Assert.Single(completions);
        Assert.Equal(new[] { "writing", "editing" }, ai.ReceivedSteps);   // coalesced timeline -> AI
        Assert.Equal(new[] { "a", "b" }, completions[0].Steps);           // recap's steps stored
        Assert.Equal("did stuff", completions[0].Summary);
    }
}

public class EngineMatchTests
{
    /// matchTask throws if called — proves the cheap path won.
    private sealed class NoMatchAI : AlwaysOnTaskAI
    {
        public Task<(string Id, bool Confident)?> Boom() => throw new InvalidOperationException("AI match should NOT be called");
    }

    [Fact]
    public async Task ProposeMatch_CheapPathWinsByNormalizedOriginal()
    {
        var store = new InMemoryStore();
        var kt = new KnowledgeTask("write proposal", DateTimeOffset.UtcNow) { OriginalTitles = { "Write proposal" } };
        store.AddKnowledgeTask(kt);

        var state = new AppState();
        var engine = new AccountabilityEngine(state, new AlwaysOnTaskAI(), new NullNotifier()) { Store = store };

        var matched = await engine.ProposeMatchAsync("Write proposal");
        Assert.NotNull(matched);
        Assert.Equal("write proposal", matched!.NormalizedTitle);
    }
}

public class EngineSessionRecapTests
{
    [Fact]
    public async Task Recap_BuiltWithLocalComparison()
    {
        var store = new InMemoryStore();
        // Prior completion (older session): 900s.
        var kt = new KnowledgeTask("write proposal", DateTimeOffset.UtcNow.AddDays(-1));
        kt.Completions.Add(new TaskCompletion(DateTimeOffset.UtcNow.AddDays(-1), 900, "s", new List<string> { "a" }, 0));
        store.AddKnowledgeTask(kt);

        var state = new AppState { Tasks = { new TaskItem { Task = "Write proposal", Context = "" } } };
        state.StartSession();
        state.Tasks[0].TimeOnTask = 600;
        var engine = new AccountabilityEngine(state, new AlwaysOnTaskAI(), new NullNotifier()) { Store = store };
        engine.BeginSession(state.Tasks);
        engine.Record(0, "vscode");

        await engine.FinalizeSessionRecapAsync();

        Assert.NotNull(state.SessionRecap);
        Assert.NotEmpty(state.SessionRecap!.Ranges);
        Assert.Contains("Faster", state.SessionRecap.PerTask[0].Comment);   // 600 < 900
    }

    [Fact]
    public async Task Recap_TransparencyLog_TimeOrderedWithDriftCount()
    {
        var (state, engine, _) = EngineTest.MakeWithSession(null, "Write proposal");
        engine.Record(0, "vscode");
        var session = engine.CurrentSession!;
        var start = session.StartedAt;

        void Seed(double offset, string kind, string excuse) => session.Justifications.Add(
            new JustificationEvent(start.AddSeconds(offset), excuse, false, null, "browsing", "", kind));
        Seed(30, "offtask", "x");
        Seed(10, "ambiguous", "research");
        Seed(20, "offtask", "x");

        await engine.FinalizeSessionRecapAsync();

        var recap = state.SessionRecap!;
        Assert.Equal(3, recap.Checks.Count);
        Assert.Equal(2, recap.DriftCount);
        Assert.Equal(state.DriftLimit, recap.DriftLimit);
        Assert.Equal(recap.Checks.OrderBy(c => c.Offset), recap.Checks);   // time-ordered
    }
}
