using AccountaBall.Core.Models;
using Xunit;

namespace AccountaBall.Core.Tests;

public class PhaseEnumTests
{
    [Fact]
    public void Phases_CoverAllMacOSCases()
    {
        // Accountability-mode phases
        _ = AppPhase.Idle; _ = AppPhase.Welcome; _ = AppPhase.Setup; _ = AppPhase.Session;
        _ = AppPhase.WhatsUp; _ = AppPhase.Ambiguous; _ = AppPhase.OffTask; _ = AppPhase.Progress;
        _ = AppPhase.Complete; _ = AppPhase.AiUnavailable;
        // FreeBall phases
        _ = AppPhase.FreeBall; _ = AppPhase.FreeBallLog; _ = AppPhase.FreeBallRecap; _ = AppPhase.FreeBallHistory;
        Assert.Equal(14, System.Enum.GetValues<AppPhase>().Length);
    }

    [Fact]
    public void BallStates_IncludeObserving()
    {
        _ = BallState.Idle; _ = BallState.OnTask; _ = BallState.OffTask; _ = BallState.Done;
        _ = BallState.Observing;
        Assert.Equal(5, System.Enum.GetValues<BallState>().Length);
    }
}
