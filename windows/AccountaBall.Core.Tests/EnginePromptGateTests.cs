using AccountaBall.Core.Models;
using Xunit;

namespace AccountaBall.Core.Tests;

public class EnginePromptGateTests
{
    [Fact]
    public void NoPromptOutsideSession_ButTimelineStillRecorded()
    {
        var (state, engine, _, clock) = EngineTest.MakeWithClock(null, "x");
        clock.Now = clock.Now.AddSeconds(3600);   // well past the settle window
        state.AppPhase = AppPhase.Progress;        // user is on the session-log screen
        engine.ProcessResult(new MultiTaskResult.OffTask("yt"));
        engine.ProcessResult(new MultiTaskResult.OffTask("yt"));
        Assert.Equal(AppPhase.Progress, state.AppPhase);            // no prompt
        Assert.Equal(2, engine.CurrentSession!.Entries.Count);     // timeline still recorded
    }
}
