using AccountaBall.Core.Models;
using AccountaBall.Core.Util;
using Xunit;

namespace AccountaBall.Core.Tests;

public class EngineAmbiguousTests
{
    [Fact]
    public void FirstAmbiguous_RaisesAsk_NotADrift()
    {
        var (state, engine, _) = EngineTest.MakeWithSession(null, "build the deck");
        engine.ResetSettleWindowToPast();
        engine.ProcessResult(new MultiTaskResult.Ambiguous("Excel budget sheet"));
        Assert.Equal(AppPhase.Ambiguous, state.AppPhase);
        Assert.Equal(0, engine.DriftCount);
    }

    [Fact]
    public void SameAmbiguousLabel_NotReAsked()
    {
        var (state, engine, _) = EngineTest.MakeWithSession(null, "build the deck");
        engine.ResetSettleWindowToPast();
        engine.ProcessResult(new MultiTaskResult.Ambiguous("Excel budget sheet"));
        Assert.Equal(AppPhase.Ambiguous, state.AppPhase);
        engine.ResumeAfterExcuse();
        engine.ResetSettleWindowToPast();
        Assert.Equal(AppPhase.Session, state.AppPhase);
        engine.ProcessResult(new MultiTaskResult.Ambiguous("Excel budget sheet"));
        Assert.Equal(AppPhase.Session, state.AppPhase);   // not re-asked
    }

    [Fact]
    public void Reject_RecordsDrift_ShowsOffCard()
    {
        var (state, engine, _) = EngineTest.MakeWithSession(null, "build the deck");
        engine.ResetSettleWindowToPast();
        engine.ProcessResult(new MultiTaskResult.Ambiguous("Excel budget sheet"));
        engine.RejectAmbiguous();
        Assert.Equal(1, engine.DriftCount);
        Assert.Equal(AppPhase.OffTask, state.AppPhase);
    }

    [Fact]
    public void Accept_LogsJustified_CreatesAllowance_NoDrift()
    {
        var (state, engine, _) = EngineTest.MakeWithSession(null, "build the deck");
        engine.ResetSettleWindowToPast();
        engine.ProcessResult(new MultiTaskResult.Ambiguous("Excel budget sheet"));
        var driftBefore = engine.DriftCount;
        engine.AcceptAmbiguous("budget for the deck");
        Assert.Contains(engine.CurrentSession!.Justifications, j => j.Kind == "ambiguous" && j.Justified);
        Assert.Equal(driftBefore, engine.DriftCount);
        var rules = engine.AllowanceRulesByIndex(state.ActiveTasks);
        Assert.Contains("budget for the deck", rules[0]);
        Assert.Equal(AppPhase.Session, state.AppPhase);
    }

    // Vouch the task: after accepting once, a DIFFERENT ambiguous label attributed
    // to the same still-incomplete task is not re-asked (treated as on-task). The
    // AI's per-screen label varies, so exact-label ask-once isn't enough.
    [Fact]
    public void VouchedTask_NewAmbiguousLabel_NotReAsked_TreatedOnTask()
    {
        var (state, engine, _) = EngineTest.MakeWithSession(null, "build the deck");
        engine.ResetSettleWindowToPast();
        engine.ProcessResult(new MultiTaskResult.Ambiguous("Excel budget sheet"));
        engine.AcceptAmbiguous("budget for the deck");   // vouches task 0
        engine.ResetSettleWindowToPast();
        Assert.Equal(AppPhase.Session, state.AppPhase);
        engine.ProcessResult(new MultiTaskResult.Ambiguous("a totally different-looking slide draft"));
        Assert.Equal(AppPhase.Session, state.AppPhase);   // not re-asked
        Assert.Equal(0, state.ActiveTaskIndex);           // treated as on-task for the vouched task
    }
}

public class EngineTimedBreakTests
{
    [Fact]
    public void DuringBreak_OffReads_NeitherPromptNorDrift()
    {
        var (state, engine, _, clock) = EngineTest.MakeWithClock(null, "write proposal");
        engine.TakeBreak();
        Assert.Equal(AppPhase.Session, state.AppPhase);
        clock.Now = clock.Now.AddSeconds(engine.SettleWindow + 1);   // past settle, within break
        engine.ProcessResult(new MultiTaskResult.OffTask("YouTube"));
        engine.ProcessResult(new MultiTaskResult.OffTask("YouTube"));
        Assert.Equal(AppPhase.Session, state.AppPhase);
        Assert.Equal(0, engine.DriftCount);
    }

    [Fact]
    public void BreakSecondsRemaining_NonNullDuring_NullAfter()
    {
        var (_, engine, _, clock) = EngineTest.MakeWithClock(null, "write proposal");
        engine.TakeBreak();
        var rem = engine.BreakSecondsRemaining;
        Assert.NotNull(rem);
        Assert.True(rem > AppConstants.BreakSeconds - 1 && rem <= AppConstants.BreakSeconds);
        clock.Now = clock.Now.AddSeconds(AppConstants.BreakSeconds + 1);
        Assert.Null(engine.BreakSecondsRemaining);
    }

    [Fact]
    public void AfterBreakElapses_NormalPromptingResumes()
    {
        var (state, engine, _, clock) = EngineTest.MakeWithClock(null, "write proposal");
        engine.TakeBreak();
        clock.Now = clock.Now.AddSeconds(AppConstants.BreakSeconds + 1);
        engine.ProcessResult(new MultiTaskResult.OffTask("YouTube"));
        clock.Now = clock.Now.AddSeconds(16);
        engine.ProcessResult(new MultiTaskResult.OffTask("YouTube"));
        Assert.Equal(AppPhase.OffTask, state.AppPhase);
        Assert.Equal(1, engine.DriftCount);
    }

    [Fact]
    public void CompletionHonoredDuringBreak()
    {
        var (state, engine, _, _) = EngineTest.MakeWithClock(null, "write proposal");
        engine.TakeBreak();
        engine.ProcessResult(new MultiTaskResult.Done(0, "shipped it"));
        Assert.True(state.Tasks[0].IsComplete);
    }

    [Fact]
    public void EarlyReturnDuringBreak_CreditsTime()
    {
        var (state, engine, _, _) = EngineTest.MakeWithClock(null, "write proposal");
        engine.TakeBreak();
        engine.ProcessResult(new MultiTaskResult.OnTask(0, "back to it"));
        Assert.Equal(AppConstants.CycleSeconds, state.Tasks[0].TimeOnTask);
        Assert.Equal(0, state.ActiveTaskIndex);
    }
}

public class EngineAutoReturnTests
{
    [Fact]
    public void IgnoredPrompt_LogsAutoReturn_NotADrift()
    {
        var (state, engine, _, _) = EngineTest.MakeWithClock(null, "write proposal");
        // Single off read inside the settle window: sets label, logs no drift.
        engine.ProcessResult(new MultiTaskResult.OffTask("Reddit"));
        Assert.Equal(0, engine.DriftCount);
        state.AppPhase = AppPhase.OffTask;

        engine.AutoReturnFromPrompt();
        Assert.Equal(AppPhase.Session, state.AppPhase);
        Assert.Equal(0, engine.DriftCount);
        var autoReturn = System.Linq.Enumerable.FirstOrDefault(
            engine.CurrentSession!.Justifications, j => j.Kind == "auto-return");
        Assert.NotNull(autoReturn);
        Assert.True(autoReturn!.Justified);
        Assert.Equal("resumed watching", autoReturn.Rule);
        Assert.Equal("Reddit", autoReturn.Activity);
    }
}

public class EngineDriftDwellTests
{
    [Fact]
    public void SameScreenUnderThreshold_DoesNotConfirm()
    {
        var (state, engine, _, _) = EngineTest.MakeWithClock(null, "write proposal");
        engine.ResetSettleWindowToPast();
        engine.ProcessResult(new MultiTaskResult.OffTask("yt"));
        engine.ProcessResult(new MultiTaskResult.OffTask("yt"));
        Assert.Equal(AppPhase.Session, state.AppPhase);
        Assert.Equal(0, engine.DriftCount);
    }

    [Fact]
    public void SameScreenAtThreshold_ConfirmsOnce()
    {
        var (state, engine, _, clock) = EngineTest.MakeWithClock(null, "write proposal");
        engine.ResetSettleWindowToPast();
        engine.ProcessResult(new MultiTaskResult.OffTask("yt"));
        clock.Now = clock.Now.AddSeconds(16);
        engine.ProcessResult(new MultiTaskResult.OffTask("yt"));
        Assert.Equal(AppPhase.OffTask, state.AppPhase);
        Assert.Equal(1, engine.DriftCount);
    }

    [Fact]
    public void SwitchingOffScreens_KeepsAccumulating()
    {
        // Continuous-dwell behavior: hopping between off-task screens (or OCR label
        // wobble on one screen) no longer resets the clock — sustained off-task time
        // still confirms. The streak only resets on a genuine return to work.
        var (state, engine, _, clock) = EngineTest.MakeWithClock(null, "write proposal");
        engine.ResetSettleWindowToPast();
        engine.ProcessResult(new MultiTaskResult.OffTask("yt"));
        clock.Now = clock.Now.AddSeconds(8);
        engine.ProcessResult(new MultiTaskResult.OffTask("twitter"));
        clock.Now = clock.Now.AddSeconds(8);   // 16s continuously off-task across two labels
        engine.ProcessResult(new MultiTaskResult.OffTask("twitter"));
        Assert.Equal(AppPhase.OffTask, state.AppPhase);
        Assert.Equal(1, engine.DriftCount);
    }

    [Fact]
    public void LabelWobbleOnOneScreen_StillConfirms()
    {
        // The bug this fixes: real OCR makes the label change every cycle even on a
        // single static off-task screen. It must still confirm.
        var (state, engine, _, clock) = EngineTest.MakeWithClock(null, "write proposal");
        engine.ResetSettleWindowToPast();
        engine.ProcessResult(new MultiTaskResult.OffTask("YouTube - video title A"));
        clock.Now = clock.Now.AddSeconds(8);
        engine.ProcessResult(new MultiTaskResult.OffTask("YouTube - video title A (recommended)"));
        clock.Now = clock.Now.AddSeconds(8);
        engine.ProcessResult(new MultiTaskResult.OffTask("YouTube - watching, 2 min in"));
        Assert.Equal(AppPhase.OffTask, state.AppPhase);
        Assert.Equal(1, engine.DriftCount);
    }

    [Fact]
    public void OnTaskBetween_RestartsStreak()
    {
        var (state, engine, _, clock) = EngineTest.MakeWithClock(null, "write proposal");
        engine.ResetSettleWindowToPast();
        engine.ProcessResult(new MultiTaskResult.OffTask("yt"));
        clock.Now = clock.Now.AddSeconds(10);
        engine.ProcessResult(new MultiTaskResult.OnTask(0, "work"));
        clock.Now = clock.Now.AddSeconds(10);
        engine.ProcessResult(new MultiTaskResult.OffTask("yt"));
        Assert.Equal(AppPhase.Session, state.AppPhase);
        Assert.Equal(0, engine.DriftCount);
    }
}
