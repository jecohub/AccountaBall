using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AccountaBall.Core.Engine;
using AccountaBall.Core.Models;
using AccountaBall.Core.Services;
using AccountaBall.Core.Storage;
using Xunit;

namespace AccountaBall.Core.Tests;

/// Returns a fixed FreeBall summary; records that it was called.
internal sealed class FreeBallFakeAI : AlwaysOnTaskAI
{
    public bool Called;
    public override Task<FreeBallSummary> SummarizeFreeBallAsync(
        IReadOnlyList<FreeBallTranscriptEntry> transcript, IReadOnlyList<FreeBallPastRecap> pastRecaps)
    {
        Called = true;
        return Task.FromResult(new FreeBallSummary(
            "N", new List<CategorySpan> { new("Coding", 5) }, "I",
            new[] { "W" }, new[] { "P" }, new[] { "C" }, new[] { "O" }));
    }
}

/// summarizeFreeBall throws (provider unreachable at End Session).
internal sealed class FreeBallFailAI : AlwaysOnTaskAI
{
    public override Task<bool> HealthCheckAsync() => Task.FromResult(false);
    public override Task<FreeBallSummary> SummarizeFreeBallAsync(
        IReadOnlyList<FreeBallTranscriptEntry> transcript, IReadOnlyList<FreeBallPastRecap> pastRecaps)
        => throw new InvalidOperationException("cannot connect to host");
}

public class FreeBallEngineTests
{
    private static (FreeBallEngine Engine, AppState State, InMemoryStore Store) Make(IAiService ai)
    {
        var state = new AppState();
        var store = new InMemoryStore();
        var engine = new FreeBallEngine(state, ai) { Store = store };
        return (engine, state, store);
    }

    [Fact]
    public void Begin_SetsPhaseAndSession()
    {
        var (engine, state, _) = Make(new FreeBallFakeAI());
        engine.Begin();
        Assert.Equal(AppPhase.FreeBall, state.AppPhase);
        Assert.NotNull(state.FreeBallStartTime);
        Assert.NotNull(engine.CurrentSession);
    }

    [Fact]
    public void Ingest_DedupsConsecutiveIdenticalReads()
    {
        var (engine, _, store) = Make(new FreeBallFakeAI());
        engine.Begin();
        engine.Ingest("editing AppDelegate.swift line 1");
        engine.Ingest("editing AppDelegate.swift line 1 ");   // same screen -> extend
        engine.Ingest("watching youtube basketball video");   // new screen -> new block
        Assert.Equal(2, store.FreeBallSessions[0].Captures.Count);
    }

    [Fact]
    public async Task End_Summarizes()
    {
        var ai = new FreeBallFakeAI();
        var (engine, state, _) = Make(ai);
        engine.Begin();
        engine.Ingest(string.Concat(System.Linq.Enumerable.Repeat("real work content ", 10)));
        await engine.EndAsync();
        Assert.True(ai.Called);
        Assert.Equal(AppPhase.FreeBallRecap, state.AppPhase);
        Assert.Equal("N", state.FreeBallRecap!.Narrative);
        Assert.Equal(new[] { "W" }, state.FreeBallRecap.WorkingOn);
        Assert.Equal(new[] { "O" }, state.FreeBallRecap.OpenThreads);
        Assert.False(state.FreeBallSummarizing);
        Assert.Null(engine.CurrentSession);
    }

    [Fact]
    public async Task End_EmptySession_SkipsAi()
    {
        var ai = new FreeBallFakeAI();
        var (engine, state, _) = Make(ai);
        engine.Begin();        // no ingest -> trivial session
        await engine.EndAsync();
        Assert.False(ai.Called);
        Assert.Equal(AppPhase.FreeBallRecap, state.AppPhase);
    }

    [Fact]
    public async Task End_AiUnavailable_MarksPendingKeepsCaptures()
    {
        var (engine, state, store) = Make(new FreeBallFailAI());
        engine.Begin();
        engine.Ingest(string.Concat(System.Linq.Enumerable.Repeat("real work content ", 10)));
        await engine.EndAsync();
        Assert.True(state.FreeBallRecap!.RecapPending);
        Assert.True(store.FreeBallSessions[0].RecapPending);
        Assert.NotEmpty(store.FreeBallSessions[0].Captures);
    }
}
