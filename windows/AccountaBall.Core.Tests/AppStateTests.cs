using AccountaBall.Core.Models;
using AccountaBall.Core.Storage;
using Xunit;

namespace AccountaBall.Core.Tests;

public class AppStateTests
{
    [Fact]
    public void InitialState_IsIdle()
    {
        var s = new AppState();
        Assert.Empty(s.Tasks);
        Assert.Equal(AppPhase.Idle, s.AppPhase);
        Assert.Null(s.ActiveTaskIndex);
        Assert.Null(s.SessionStartTime);
        Assert.False(s.IsCapturing);
        Assert.Equal(BallState.Idle, s.BallState);
    }

    [Fact]
    public void ActiveTasks_ExcludesCompleted()
    {
        var s = new AppState();
        s.Tasks = new() { new TaskItem { Task = "a", Context = "ctx" }, new TaskItem { Task = "b", Context = "ctx" } };
        Assert.Equal(2, s.ActiveTasks.Count);
        s.Tasks[0].IsComplete = true;
        Assert.Single(s.ActiveTasks);
    }

    [Fact]
    public void AllTasksComplete_TrueOnlyWhenAllDoneAndNonEmpty()
    {
        var s = new AppState();
        s.Tasks = new() { new TaskItem { Task = "a", Context = "b" } };
        Assert.False(s.AllTasksComplete);
        s.Tasks[0].IsComplete = true;
        Assert.True(s.AllTasksComplete);
        Assert.False(new AppState().AllTasksComplete);
    }

    [Fact]
    public void StartSession_SetsSessionFields()
    {
        var s = new AppState();
        s.Tasks = new() { new TaskItem { Task = "a", Context = "b" } };
        s.StartSession();
        Assert.Equal(AppPhase.Session, s.AppPhase);
        Assert.NotNull(s.SessionStartTime);
        Assert.True(s.IsCapturing);
        Assert.Equal(BallState.OnTask, s.BallState);
    }

    [Fact]
    public void CompleteTaskAt_TasksRemain_SessionContinues()
    {
        var s = new AppState();
        s.Tasks = new() { new TaskItem { Task = "a", Context = "b" }, new TaskItem { Task = "c", Context = "d" } };
        s.StartSession();
        s.CompleteTaskAt(0);
        Assert.True(s.Tasks[0].IsComplete);
        Assert.Equal(AppPhase.Session, s.AppPhase);
    }

    [Fact]
    public void CompleteTaskAt_AllDone_TriggersComplete()
    {
        var s = new AppState();
        s.Tasks = new() { new TaskItem { Task = "a", Context = "b" } };
        s.StartSession();
        s.CompleteTaskAt(0);
        Assert.Equal(AppPhase.Complete, s.AppPhase);
        Assert.False(s.IsCapturing);
        Assert.Null(s.SessionStartTime);
        Assert.NotNull(s.LastSessionDuration);
    }

    [Fact]
    public void CompleteTaskAt_OutOfBounds_IsNoOp()
    {
        var s = new AppState();
        s.Tasks = new() { new TaskItem { Task = "a", Context = "b" } };
        s.CompleteTaskAt(99);
        Assert.False(s.Tasks[0].IsComplete);
    }

    [Fact]
    public void Tasks_SurviveSaveLoad_AndReset()
    {
        var store = new InMemoryKeyValueStore();
        var s8 = new AppState(store);
        s8.Tasks = new() { new TaskItem { Task = "persist me", Context = "ctx", IsComplete = true, TimeOnTask = 99 } };
        s8.SaveTasks();
        var s9 = new AppState(store);
        s9.LoadTasks();
        Assert.Equal("persist me", s9.Tasks[0].Task);
        Assert.False(s9.Tasks[0].IsComplete);   // loadTasks resets
        Assert.Equal(0, s9.Tasks[0].TimeOnTask); // loadTasks resets
    }

    [Fact]
    public void ClearSavedTasks_EmptiesAndDoesNotSurviveLoad()
    {
        var store = new InMemoryKeyValueStore();
        var s10 = new AppState(store);
        s10.Tasks = new() { new TaskItem { Task = "x", Context = "y" } };
        s10.SaveTasks();
        s10.ClearSavedTasks();
        Assert.Empty(s10.Tasks);
        var s11 = new AppState(store);
        s11.LoadTasks();
        Assert.Empty(s11.Tasks);
    }

    [Fact]
    public void DriftLimit_ClampsAndPersists()
    {
        var store = new InMemoryKeyValueStore();
        var s = new AppState(store);
        s.DriftLimit = 99;
        Assert.Equal(10, s.DriftLimit);   // clamped high
        s.DriftLimit = 0;
        Assert.Equal(1, s.DriftLimit);    // clamped low
        s.DriftLimit = 5;
        var s2 = new AppState(store);
        s2.LoadDriftLimit();
        Assert.Equal(5, s2.DriftLimit);   // persisted across instances
    }

    [Fact]
    public void LoadDriftLimit_UnsetDefaultsToThree()
    {
        var s = new AppState(new InMemoryKeyValueStore());
        s.LoadDriftLimit();
        Assert.Equal(3, s.DriftLimit);
    }

    [Fact]
    public void FreeBall_Defaults()
    {
        var s = new AppState();
        Assert.Null(s.FreeBallStartTime);
        Assert.False(s.FreeBallSummarizing);
        Assert.False(s.FreeBallViewingHistory);
    }
}
