using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using AccountaBall.Core.Storage;

namespace AccountaBall.Core.Models;

/// The app's mutable session state. Port of Swift `AppState` (the SwiftUI
/// `@Published` bindings become plain properties — UI notification is an App-layer
/// concern). `UserDefaults` is abstracted behind <see cref="IKeyValueStore"/>.
///
/// NOTE: the recap dictionary (`Recaps`/TaskRecap) and `FreeBallRecap` property
/// are added with their types in M2.5/M2.6.
public sealed class AppState
{
    private const string TasksKey = "accountaball.tasks.v2";
    private const string DriftLimitKey = "accountaball.driftLimit.v1";

    private readonly IKeyValueStore _store;

    public AppState(IKeyValueStore? store = null) => _store = store ?? new InMemoryKeyValueStore();

    public List<TaskItem> Tasks { get; set; } = new();
    public AppPhase AppPhase { get; set; } = AppPhase.Idle;
    public int? ActiveTaskIndex { get; set; }
    public DateTimeOffset? SessionStartTime { get; set; }
    public bool IsCapturing { get; set; }
    public BallState BallState { get; set; } = BallState.Idle;
    public List<TaskSession> SessionLog { get; set; } = new();
    public string? SetupHint { get; set; }
    public string? AiUnavailableHint { get; set; }
    public SessionRecap? SessionRecap { get; set; }
    public double? LastSessionDuration { get; set; }
    public AllowanceConfirm? PendingAllowanceConfirm { get; set; }
    /// AI recap per finished task, keyed by task title (written by the engine on
    /// completion; read by the recap UI).
    public Dictionary<string, TaskRecap> Recaps { get; } = new();

    // FreeBall passive-mode bridges.
    public DateTimeOffset? FreeBallStartTime { get; set; }
    public bool FreeBallSummarizing { get; set; }
    public bool FreeBallViewingHistory { get; set; }
    public FreeBallRecap? FreeBallRecap { get; set; }

    private int _driftLimit = 3;

    /// Pre-committed drift budget. Set ONLY at setup; clamped to [1,10] and
    /// persisted on every set (mirrors the Swift didSet).
    public int DriftLimit
    {
        get => _driftLimit;
        set
        {
            _driftLimit = Math.Min(Math.Max(value, 1), 10);
            _store.SetInt(DriftLimitKey, _driftLimit);
        }
    }

    public void LoadDriftLimit()
    {
        var v = _store.GetInt(DriftLimitKey);
        DriftLimit = v == 0 ? 3 : Math.Min(Math.Max(v, 1), 10);   // 0 == unset -> default 3
    }

    public IReadOnlyList<TaskItem> ActiveTasks => Tasks.Where(t => !t.IsComplete).ToList();
    public bool AllTasksComplete => Tasks.Count > 0 && Tasks.All(t => t.IsComplete);

    public void StartSession()
    {
        SessionStartTime = DateTimeOffset.UtcNow;
        LastSessionDuration = null;
        SessionRecap = null;
        IsCapturing = true;
        AppPhase = AppPhase.Session;
        BallState = BallState.OnTask;
    }

    public void EndSession()
    {
        IsCapturing = false;
        SessionStartTime = null;
        ActiveTaskIndex = null;
        BallState = BallState.Idle;
    }

    public void CompleteTaskAt(int index)
    {
        if (index < 0 || index >= Tasks.Count) return;
        Tasks[index].IsComplete = true;
        if (AllTasksComplete)
        {
            // Freeze the total duration before EndSession() nils SessionStartTime.
            if (SessionStartTime is { } start)
            {
                LastSessionDuration = (DateTimeOffset.UtcNow - start).TotalSeconds;
            }
            EndSession();
            AppPhase = AppPhase.Complete;
        }
    }

    public void SaveTasks()
    {
        _store.SetData(TasksKey, JsonSerializer.SerializeToUtf8Bytes(Tasks));
    }

    public void LoadTasks()
    {
        var data = _store.GetData(TasksKey);
        if (data is null) return;
        var decoded = JsonSerializer.Deserialize<List<TaskItem>>(data);
        if (decoded is null) return;
        Tasks = decoded.Select(t => t with { IsComplete = false, TimeOnTask = 0 }).ToList();
    }

    public void ClearSavedTasks()
    {
        _store.Remove(TasksKey);
        Tasks = new();
    }

    /// Legacy compat used by the engine: the first active task's title.
    public string CurrentTask => ActiveTasks.FirstOrDefault()?.Task ?? string.Empty;
}

/// A one-time prompt to confirm a revived allowance from a previously-completed
/// task brought back into this session. Port of Swift `AllowanceConfirm`.
public sealed record AllowanceConfirm(string TaskTitle, string Rule)
{
    public Guid Id { get; } = Guid.NewGuid();
}
