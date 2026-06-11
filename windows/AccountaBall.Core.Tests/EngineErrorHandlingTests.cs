using AccountaBall.Core.Models;
using Xunit;

namespace AccountaBall.Core.Tests;

public class EngineErrorHandlingTests
{
    [Fact]
    public void NullCycle_IsNotOffTask()
    {
        var (state, engine, _) = EngineTest.MakeWithSession(null, "write");
        // Two failed cycles (null) must NOT escalate to the off-task prompt.
        engine.ProcessCycle(null);
        engine.ProcessCycle(null);
        Assert.Equal(AppPhase.Session, state.AppPhase);
        Assert.NotEqual(BallState.OffTask, state.BallState);
    }
}
