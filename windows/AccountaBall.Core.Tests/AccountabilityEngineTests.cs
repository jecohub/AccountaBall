using System;
using AccountaBall.Core.Models;
using AccountaBall.Core.Util;
using Xunit;

namespace AccountaBall.Core.Tests;

public class AccountabilityEngineTests
{
    // ── Decision logic (no store; signals injected via ProcessResult) ──────────

    [Fact]
    public void SingleOffTask_DoesNotFlip()
    {
        var (state, engine) = EngineTest.MakeNoStore("write proposal", "review slides");
        engine.ProcessResult(new MultiTaskResult.OffTask(""));
        Assert.Equal(BallState.OnTask, state.BallState);
        Assert.Equal(AppPhase.Session, state.AppPhase);
    }

    [Fact]
    public void TwoConsecutiveOffTask_OnSameScreenPastDwell_Flips()
    {
        var (state, engine) = EngineTest.MakeNoStore("write proposal", "review slides");
        var clock = new MutableClock();
        engine.Now = clock.Func;
        engine.ProcessResult(new MultiTaskResult.OffTask(""));
        clock.Now = clock.Now.AddSeconds(16);   // hold the SAME off-task screen >=15s
        engine.ProcessResult(new MultiTaskResult.OffTask(""));
        Assert.Equal(BallState.OffTask, state.BallState);
        Assert.Equal(AppPhase.OffTask, state.AppPhase);
    }

    [Fact]
    public void OnTaskBetweenOffReads_ResetsSuspicion()
    {
        var (state, engine) = EngineTest.MakeNoStore("write proposal", "review slides");
        engine.ProcessResult(new MultiTaskResult.OffTask(""));
        engine.ProcessResult(new MultiTaskResult.OnTask(0, ""));
        engine.ProcessResult(new MultiTaskResult.OffTask(""));
        Assert.Equal(BallState.OnTask, state.BallState);
    }

    [Fact]
    public void OnTask_SetsActiveIndexAndBall()
    {
        var (state, engine) = EngineTest.MakeNoStore("write proposal", "review slides");
        engine.ProcessResult(new MultiTaskResult.OnTask(1, ""));
        Assert.Equal(1, state.ActiveTaskIndex);
        Assert.Equal(BallState.OnTask, state.BallState);
    }

    [Fact]
    public void OnTask_AccumulatesTimePerCycleAndPerTask()
    {
        var (state, engine) = EngineTest.MakeNoStore("write proposal", "review slides");
        engine.ProcessResult(new MultiTaskResult.OnTask(0, ""));
        Assert.Equal(AppConstants.CycleSeconds, state.Tasks[0].TimeOnTask);
        engine.ProcessResult(new MultiTaskResult.OnTask(0, ""));
        Assert.Equal(AppConstants.CycleSeconds * 2, state.Tasks[0].TimeOnTask);

        var (s2, e2) = EngineTest.MakeNoStore("write proposal", "review slides");
        e2.ProcessResult(new MultiTaskResult.OnTask(1, ""));
        Assert.Equal(AppConstants.CycleSeconds, s2.Tasks[1].TimeOnTask);
        Assert.Equal(0, s2.Tasks[0].TimeOnTask);
    }

    [Fact]
    public void Done_MarksComplete_AndAllDoneCompletesSession()
    {
        var (state, engine) = EngineTest.MakeNoStore("write proposal", "review slides");
        engine.ProcessResult(new MultiTaskResult.Done(0, ""));
        Assert.True(state.Tasks[0].IsComplete);
        Assert.Equal(AppPhase.Session, state.AppPhase);   // one task left
        engine.ProcessResult(new MultiTaskResult.Done(1, ""));
        Assert.Equal(AppPhase.Complete, state.AppPhase);
    }

    [Fact]
    public void StillOffWhilePromptUp_KeepsPrompt_ReturningAutoResumes()
    {
        var (state, engine) = EngineTest.MakeNoStore("write proposal", "review slides");
        var clock = new MutableClock();
        engine.Now = clock.Func;
        engine.ProcessResult(new MultiTaskResult.OffTask(""));
        clock.Now = clock.Now.AddSeconds(16);
        engine.ProcessResult(new MultiTaskResult.OffTask(""));
        Assert.Equal(AppPhase.OffTask, state.AppPhase);
        engine.ProcessResult(new MultiTaskResult.OffTask(""));        // still off
        Assert.Equal(AppPhase.OffTask, state.AppPhase);
        engine.ProcessResult(new MultiTaskResult.OnTask(0, ""));      // returns to a task
        Assert.Equal(AppPhase.Session, state.AppPhase);
        Assert.Equal(0, state.ActiveTaskIndex);
        Assert.Equal(BallState.OnTask, state.BallState);
    }

    [Fact]
    public void ResumeAfterExcuse_RestoresSessionAndResetsSuspicion()
    {
        var (state, engine) = EngineTest.MakeNoStore("write proposal", "review slides");
        var clock = new MutableClock();
        engine.Now = clock.Func;
        engine.ProcessResult(new MultiTaskResult.OffTask(""));
        clock.Now = clock.Now.AddSeconds(16);
        engine.ProcessResult(new MultiTaskResult.OffTask(""));
        Assert.Equal(AppPhase.OffTask, state.AppPhase);
        engine.ResumeAfterExcuse();
        Assert.Equal(BallState.OnTask, state.BallState);
        Assert.Equal(AppPhase.Session, state.AppPhase);
        engine.ProcessResult(new MultiTaskResult.OffTask(""));
        Assert.Equal(AppPhase.Session, state.AppPhase);   // single read after resume can't confirm
    }

    // ── Session lifecycle (with store) ─────────────────────────────────────────

    [Fact]
    public void BeginSession_RecordEntries_EndSession()
    {
        var (_, engine, _) = EngineTest.MakeWithSession(null, "test");
        Assert.NotNull(engine.CurrentSession);
        Assert.Equal(new[] { "test" }, engine.CurrentSession!.TaskTitles);

        engine.Record(0, "x");
        engine.Record(0, "y");
        Assert.Equal(2, engine.CurrentSession.Entries.Count);
        Assert.Equal(new[] { "x", "y" }, engine.CurrentSession.Entries.ConvertAll(e => e.Label));
        Assert.All(engine.CurrentSession.Entries, e => Assert.Equal(0, e.TaskIndex));

        engine.EndSession();
        Assert.Null(engine.CurrentSession);
    }

    [Fact]
    public void LabelCapture_OnAndOffTask()
    {
        var (_, engine, _) = EngineTest.MakeWithSession(null, "test");
        engine.ProcessResult(new MultiTaskResult.OnTask(0, "writing"));
        Assert.Equal("writing", engine.LastActivityLabel);
        engine.ProcessResult(new MultiTaskResult.OffTask("twitter"));
        Assert.Equal("twitter", engine.LastActivityLabel);
        Assert.Contains(engine.CurrentSession!.Entries, e => e.Label == "twitter");
    }

    [Fact]
    public void NoStore_LifecycleIsNoOp()
    {
        var (_, engine) = EngineTest.MakeNoStore("t");
        engine.BeginSession();
        Assert.Null(engine.CurrentSession);
        engine.Record(0, "x");      // no-op, no crash
        engine.EndSession();        // no-op, no crash
        Assert.Null(engine.CurrentSession);
    }

    // ── Drift count / commitment ───────────────────────────────────────────────

    [Fact]
    public void DriftCount_CountsOnlyOffTask_CommitmentBrokenAtLimit()
    {
        var (state, engine, _) = EngineTest.MakeWithSession(null, "write proposal");
        var session = engine.CurrentSession!;
        void Seed(string kind) => session.Justifications.Add(
            new JustificationEvent(DateTimeOffset.UtcNow, "x", false, null, "y", "", kind));
        Seed("offtask");
        Seed("offtask");
        Seed("ambiguous");
        Assert.Equal(2, engine.DriftCount);
        state.DriftLimit = 2;
        Assert.True(engine.CommitmentBroken);
        state.DriftLimit = 3;
        Assert.False(engine.CommitmentBroken);
    }

    [Fact]
    public void EnteringOffTask_LogsExactlyOneDrift_StillOffDoesNotRelog()
    {
        var (state, engine, _) = EngineTest.MakeWithSession(null, "write proposal");
        var clock = new MutableClock();
        engine.Now = clock.Func;
        engine.ResetSettleWindowToPast();
        engine.ProcessResult(new MultiTaskResult.OffTask("YouTube music"));
        clock.Now = clock.Now.AddSeconds(16);
        engine.ProcessResult(new MultiTaskResult.OffTask("YouTube music"));
        Assert.Equal(1, engine.DriftCount);
        Assert.Equal(AppPhase.OffTask, state.AppPhase);
        engine.ProcessResult(new MultiTaskResult.OffTask("YouTube music"));   // still off, prompt up
        Assert.Equal(1, engine.DriftCount);
    }

    [Fact]
    public void ReturningToWork_DoesNotInflateDrift_LogsAutoReturn()
    {
        var (state, engine, _) = EngineTest.MakeWithSession(null, "write proposal");
        var clock = new MutableClock();
        engine.Now = clock.Func;
        engine.ResetSettleWindowToPast();
        engine.ProcessResult(new MultiTaskResult.OffTask("YouTube"));
        clock.Now = clock.Now.AddSeconds(16);
        engine.ProcessResult(new MultiTaskResult.OffTask("YouTube"));
        Assert.Equal(1, engine.DriftCount);
        Assert.Equal(AppPhase.OffTask, state.AppPhase);

        engine.ProcessResult(new MultiTaskResult.OnTask(0, "back to it"));
        Assert.Equal(1, engine.DriftCount);
        Assert.Contains(engine.CurrentSession!.Justifications, j => j.Kind == "auto-return");
        Assert.Equal(AppPhase.Session, state.AppPhase);
    }
}
