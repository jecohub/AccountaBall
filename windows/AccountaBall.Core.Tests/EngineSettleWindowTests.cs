using AccountaBall.Core.Engine;
using AccountaBall.Core.Models;
using AccountaBall.Core.Services;
using AccountaBall.Core.Storage;
using AccountaBall.Core.Util;
using Xunit;

namespace AccountaBall.Core.Tests;

public class EngineSettleWindowTests
{
    [Fact]
    public void SettleWindow_SuppressesEarlyPrompt_PromptsAfter()
    {
        var (state, engine, _, clock) = EngineTest.MakeWithClock(null, "x");
        engine.ProcessResult(new MultiTaskResult.OffTask("yt"));
        engine.ProcessResult(new MultiTaskResult.OffTask("yt"));
        Assert.Equal(AppPhase.Session, state.AppPhase);   // inside the window
        clock.Now = clock.Now.AddSeconds(16);
        engine.ProcessResult(new MultiTaskResult.OffTask("yt"));
        engine.ProcessResult(new MultiTaskResult.OffTask("yt"));
        Assert.Equal(AppPhase.OffTask, state.AppPhase);   // after the window
    }

    // Build an engine with an injected clock but NO session (grace tests inject
    // off-task reads and resume directly, never calling BeginSession).
    private static (AppState, AccountabilityEngine, MutableClock) MakeGraceEngine()
    {
        var state = new AppState { Tasks = { new TaskItem { Task = "x", Context = "" } } };
        state.StartSession();
        var engine = new AccountabilityEngine(state, new AlwaysOnTaskAI(), new NullNotifier()) { Store = new InMemoryStore() };
        var clock = new MutableClock();
        engine.Now = clock.Func;
        return (state, engine, clock);
    }

    [Fact]
    public void ActivityGrace_SuppressesSameActivity_ReChecksOnSwitch()
    {
        var (state, engine, clock) = MakeGraceEngine();
        engine.ProcessResult(new MultiTaskResult.OffTask("watching youtube"));   // sets lastActivityLabel
        engine.ResumeAfterExcuse(graceForCurrentActivity: true);                 // grace +120s for that activity
        clock.Now = clock.Now.AddSeconds(engine.SettleWindow + 1);
        engine.ProcessResult(new MultiTaskResult.OffTask("watching youtube"));
        engine.ProcessResult(new MultiTaskResult.OffTask("watching youtube"));
        Assert.Equal(AppPhase.Session, state.AppPhase);   // same activity suppressed
        // Switch activity -> grace ends; new screen must be held >=15s to confirm.
        engine.ProcessResult(new MultiTaskResult.OffTask("scrolling twitter"));
        clock.Now = clock.Now.AddSeconds(engine.SettleWindow + 1);
        engine.ProcessResult(new MultiTaskResult.OffTask("scrolling twitter"));
        Assert.Equal(AppPhase.OffTask, state.AppPhase);
    }

    [Fact]
    public void ActivityGrace_ExpiresAfterWindow()
    {
        var (state, engine, clock) = MakeGraceEngine();
        engine.ProcessResult(new MultiTaskResult.OffTask("watching youtube"));
        engine.ResumeAfterExcuse(graceForCurrentActivity: true);                 // graceUntil = now+120
        clock.Now = clock.Now.AddSeconds(AppConstants.ContinueAnywayGraceSeconds + 1);
        engine.ProcessResult(new MultiTaskResult.OffTask("watching youtube"));
        clock.Now = clock.Now.AddSeconds(engine.SettleWindow + 1);
        engine.ProcessResult(new MultiTaskResult.OffTask("watching youtube"));
        Assert.Equal(AppPhase.OffTask, state.AppPhase);   // same activity prompts again after grace lapses
    }
}
