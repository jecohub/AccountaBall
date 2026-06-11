using System;
using System.Threading.Tasks;
using AccountaBall.Core.Engine;
using AccountaBall.Core.Models;
using AccountaBall.Core.Services;
using AccountaBall.Core.Storage;
using Xunit;

namespace AccountaBall.Core.Tests;

public class EngineAvailabilityTests
{
    [Fact]
    public async Task PauseAndResume()
    {
        var ai = new FlakyAI();
        var (state, engine, _) = EngineTest.MakeWithSession(ai, "x");
        Assert.False(await ai.HealthCheckAsync());   // provider is down

        engine.EnterAIUnavailable();
        Assert.Equal(AppPhase.AiUnavailable, state.AppPhase);
        Assert.NotNull(state.AiUnavailableHint);
        Assert.False(state.IsCapturing);

        ai.Recover();
        Assert.True(await ai.HealthCheckAsync());
        engine.RecoverFromAIUnavailable();
        Assert.Equal(AppPhase.Session, state.AppPhase);
        Assert.Null(state.AiUnavailableHint);
        Assert.True(state.IsCapturing);              // false->true edge restarts the loop
    }

    [Fact]
    public void Recover_ReturnsToOriginPhase()
    {
        var ai = new FlakyAI();
        var state = new AppState { AppPhase = AppPhase.Welcome };   // fresh launch, no session
        var engine = new AccountabilityEngine(state, ai, new NullNotifier()) { Store = new InMemoryStore() };

        engine.EnterAIUnavailable();
        Assert.Equal(AppPhase.AiUnavailable, state.AppPhase);
        ai.Recover();
        engine.RecoverFromAIUnavailable();
        Assert.Equal(AppPhase.Welcome, state.AppPhase);   // not .Session
        Assert.False(state.IsCapturing);
    }

    [Fact]
    public void BenignCancellation_Classification()
    {
        Assert.True(AccountabilityEngine.IsBenignCancellation(new OperationCanceledException()));
        Assert.True(AccountabilityEngine.IsBenignCancellation(new TaskCanceledException()));
        Assert.False(AccountabilityEngine.IsBenignCancellation(new InvalidOperationException("cannot connect")));
        Assert.False(AccountabilityEngine.IsBenignCancellation(new Exception("other")));
    }
}
