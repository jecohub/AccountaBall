using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AccountaBall.Core.Models;
using AccountaBall.Core.Services;
using AccountaBall.Core.Storage;
using AccountaBall.Core.Util;

namespace AccountaBall.Core.Engine;

/// The deterministic accountability engine ("the model perceives, Swift/C# decides").
/// Faithful port of Swift `AccountabilityEngine`
/// (src/Sources/AccountaBall/Engine/AccountabilityEngine.swift).
///
/// Scope of this file (M2.4): per-cycle decisions, the session lifecycle, the
/// drift engine, AMBIGUOUS ask-once, the timed break, settle/grace windows,
/// allowances, and the AI-unavailable pause/recover. DEFERRED to M2.5:
/// summarizeCompletion / finalizeSessionRecap / proposeMatch / the
/// allowance-confirm-on-reuse queue. DROPPED (macOS-only): the App-Nap activity
/// token. The live capture loop (start) is wired in the Platform/App layer (M3/M4);
/// the engine is driven per-cycle via <see cref="ProcessCycle"/> / <see cref="ProcessResult"/>.
public sealed class AccountabilityEngine
{
    private readonly AppState _state;
    private readonly IAiService _ai;
    private readonly INotifier _notifier;

    /// Durable store. Null = the no-op stub path (mirrors a nil SwiftData context):
    /// beginSession/record/logCheck become no-ops.
    public IStore? Store { get; set; }

    /// Injectable clock for deterministic settle/dwell/break tests.
    public Func<DateTimeOffset> Now { get; set; } = () => DateTimeOffset.UtcNow;
    public double SettleWindow { get; } = 15;

    private DateTimeOffset _settleUntil = DateTimeOffset.MinValue;
    private int _suspicionCount;
    private DateTimeOffset? _offTaskStreakStart;
    private string _offTaskStreakActivity = string.Empty;
    private string? _graceActivity;
    private DateTimeOffset _graceUntil = DateTimeOffset.MinValue;
    private DateTimeOffset _breakUntil = DateTimeOffset.MinValue;
    private readonly HashSet<string> _askedActivities = new();
    private AppPhase _phaseBeforeUnavailable = AppPhase.Session;

    public WorkSession? CurrentSession { get; private set; }
    public string LastActivityLabel { get; private set; } = string.Empty;

    public AccountabilityEngine(AppState state, IAiService ai, INotifier notifier)
    {
        _state = state;
        _ai = ai;
        _notifier = notifier;
    }

    // MARK: - Derived drift state (tamper-proof: read from the persisted record)

    /// Confirmed drifts this session = off-task JustificationEvents. Derived so it
    /// can never desync from the recap.
    public int DriftCount => CurrentSession?.Justifications.Count(j => j.Kind == "offtask") ?? 0;

    /// The pre-committed limit was reached. `state.DriftLimit` is set only at setup,
    /// so this is tamper-proof.
    public bool CommitmentBroken => DriftCount >= _state.DriftLimit;

    // MARK: - Windows / guards

    private bool InSettleWindow => Now() < _settleUntil;
    private bool InActivityGrace => _graceActivity != null && Now() < _graceUntil;
    private bool OnBreak => Now() < _breakUntil;

    /// Seconds left on the current break, or null if not on one (UI countdown).
    public double? BreakSecondsRemaining => OnBreak ? (_breakUntil - Now()).TotalSeconds : null;

    public void ResetSettleWindow() => _settleUntil = Now().AddSeconds(SettleWindow);

    /// Test seam: collapse the settle window so off-task reads aren't suppressed.
    public void ResetSettleWindowToPast() => _settleUntil = DateTimeOffset.MinValue;

    private void ClearActivityGrace()
    {
        _graceActivity = null;
        _graceUntil = DateTimeOffset.MinValue;
    }

    private void ClearOffTaskStreak()
    {
        _offTaskStreakStart = null;
        _offTaskStreakActivity = string.Empty;
    }

    /// Reset volatile per-cycle state. (The macOS App-Nap token + capture-loop
    /// cancellation it also did are Platform concerns.)
    public void Stop()
    {
        _suspicionCount = 0;
        ClearOffTaskStreak();
    }

    // MARK: - Session lifecycle

    public void BeginSession(IReadOnlyList<TaskItem>? tasks = null)
    {
        if (Store is not { } store) return;
        var session = new WorkSession(DateTimeOffset.UtcNow)
        {
            TaskTitles = (tasks ?? Array.Empty<TaskItem>()).Select(t => t.Task).ToList(),
        };
        store.AddSession(session);
        store.Save();
        CurrentSession = session;
        _askedActivities.Clear();
        ClearOffTaskStreak();
        ResetSettleWindow();
    }

    public void EndSession()
    {
        if (CurrentSession is not { } session || Store is not { } store) return;
        session.EndedAt = DateTimeOffset.UtcNow;
        store.Save();
        CurrentSession = null;
    }

    private void LogCheck(string kind, bool justified, string activity, string excuse, string rule, int? taskIndex)
    {
        if (CurrentSession is not { } session || Store is not { } store) return;
        session.Justifications.Add(new JustificationEvent(
            DateTimeOffset.UtcNow, excuse, justified, taskIndex, activity, rule, kind));
        store.Save();
    }

    public void Record(int? taskIndex, string label)
    {
        if (CurrentSession is not { } session || Store is not { } store) return;
        session.Entries.Add(new TimelineEntry(DateTimeOffset.UtcNow, taskIndex, label));
        store.Save();
    }

    // MARK: - Resume / break / auto-return

    /// Resume watching after an off-task prompt. With grace, grant an
    /// activity-scoped breather for the current off-task activity.
    public void ResumeAfterExcuse(bool graceForCurrentActivity = false)
    {
        _suspicionCount = 0;
        ClearOffTaskStreak();
        _state.BallState = BallState.OnTask;
        _state.AppPhase = AppPhase.Session;
        ResetSettleWindow();
        if (graceForCurrentActivity && !string.IsNullOrEmpty(LastActivityLabel))
        {
            _graceActivity = LastActivityLabel;
            _graceUntil = Now().AddSeconds(AppConstants.ContinueAnywayGraceSeconds);
        }
        else
        {
            ClearActivityGrace();
        }
    }

    /// A prompt ignored past the response window: record an honest auto-return
    /// (not a drift) and resume.
    public void AutoReturnFromPrompt()
    {
        LogCheck("auto-return", justified: true, activity: LastActivityLabel,
                 excuse: "(no response)", rule: "resumed watching", taskIndex: null);
        ResumeAfterExcuse();
    }

    /// User chose the timed break on a confirmed drift. Ball goes quiet until the
    /// window elapses.
    public void TakeBreak()
    {
        _breakUntil = Now().AddSeconds(AppConstants.BreakSeconds);
        _suspicionCount = 0;
        ClearOffTaskStreak();
        _state.BallState = BallState.OnTask;
        _state.AppPhase = AppPhase.Session;
        ResetSettleWindow();
    }

    // MARK: - Helpers

    /// Two activity labels refer to the same screen (case/whitespace-insensitive).
    public static bool ActivityMatches(string a, string b)
    {
        static string Norm(string s) => s.Trim().ToLowerInvariant();
        return Norm(a) == Norm(b);
    }

    /// True for cancellation-class errors (we cancelled the in-flight request as
    /// part of the normal capture lifecycle). Must NOT be treated as an outage.
    public static bool IsBenignCancellation(Exception error) => error is OperationCanceledException;

    // MARK: - AI unavailable

    public void EnterAIUnavailable(Exception? reason = null)
    {
        if (_state.AppPhase == AppPhase.AiUnavailable) return;
        _phaseBeforeUnavailable = _state.AppPhase;
        Stop();
        // Drop capturing so recovery's true is a real false->true edge for the watcher.
        _state.IsCapturing = false;
        _state.AiUnavailableHint = AiHint();
        _state.BallState = BallState.Idle;
        _state.AppPhase = AppPhase.AiUnavailable;
        // Health polling is wired in the App/Platform layer (M3/M4).
    }

    private static string AiHint() =>
        "Can't reach the AI. If using Ollama, run it and pull the model (e.g. `ollama run qwen2.5:7b`); or set AI_PROVIDER/OPENROUTER_API_KEY.";

    public void RecoverFromAIUnavailable()
    {
        if (_state.AppPhase != AppPhase.AiUnavailable) return;
        _state.AiUnavailableHint = null;
        var target = _phaseBeforeUnavailable == AppPhase.AiUnavailable ? AppPhase.Welcome : _phaseBeforeUnavailable;
        _state.AppPhase = target;
        if (target == AppPhase.Session)
        {
            _state.BallState = BallState.OnTask;
            _state.IsCapturing = true;
            ResetSettleWindow();
        }
        else
        {
            _state.BallState = BallState.Idle;
        }
    }

    // MARK: - Per-cycle processing

    /// Process one classification outcome. `null` means the AI call failed — skip
    /// the cycle entirely (no suspicion bump, never off-task).
    public void ProcessCycle(MultiTaskResult? result)
    {
        if (result is null) return;
        ProcessResult(result);
    }

    public void ProcessResult(MultiTaskResult result)
    {
        // While the off-task prompt is up, only an on-task read auto-dismisses it.
        if (_state.AppPhase == AppPhase.OffTask)
        {
            if (result is MultiTaskResult.OnTask back)
            {
                LastActivityLabel = back.Label;
                Record(back.Index, back.Label);
                _state.ActiveTaskIndex = back.Index;
                CreditTime(back.Index);
                // Log the self-resolved return (justified; not a drift).
                if (CurrentSession is { } session && Store is { } store)
                {
                    session.Justifications.Add(new JustificationEvent(
                        DateTimeOffset.UtcNow, "(returned to work)", justified: true,
                        inferredTaskIndex: back.Index, activity: back.Label,
                        rule: "returned to work", kind: "auto-return"));
                    store.Save();
                }
                ResumeAfterExcuse();
            }
            return;
        }

        // Timed break: quiet for all reads. Still honor completion and credit a
        // genuine early return to work.
        if (OnBreak)
        {
            switch (result)
            {
                case MultiTaskResult.Done d:
                    CompleteTask(d.Index, d.Label);
                    break;
                case MultiTaskResult.OnTask o:
                    LastActivityLabel = o.Label;
                    Record(o.Index, o.Label);
                    _state.ActiveTaskIndex = o.Index;
                    CreditTime(o.Index);
                    break;
            }
            return;
        }

        switch (result)
        {
            case MultiTaskResult.OnTask onTask:
                LastActivityLabel = onTask.Label;
                Record(onTask.Index, onTask.Label);
                _suspicionCount = 0;
                ClearOffTaskStreak();
                _state.ActiveTaskIndex = onTask.Index;
                _state.BallState = BallState.OnTask;
                CreditTime(onTask.Index);
                break;

            case MultiTaskResult.Ambiguous amb:
                LastActivityLabel = amb.Label;
                Record(null, amb.Label);
                _suspicionCount = 0;                 // ambiguous is not a confirmed drift
                ClearOffTaskStreak();
                _state.ActiveTaskIndex = null;
                var key = amb.Label.Trim().ToLowerInvariant();
                if (_askedActivities.Contains(key) || (InActivityGrace && ActivityMatches(amb.Label, _graceActivity ?? "")))
                    return;
                if (InSettleWindow || _state.AppPhase != AppPhase.Session) return;
                _askedActivities.Add(key);
                _state.BallState = BallState.OffTask;
                _state.AppPhase = AppPhase.Ambiguous;
                break;

            case MultiTaskResult.OffTask off:
                LastActivityLabel = off.Label;
                Record(null, off.Label);
                if (InActivityGrace)
                {
                    if (ActivityMatches(off.Label, _graceActivity ?? ""))
                    {
                        _state.ActiveTaskIndex = null;
                        return;
                    }
                    ClearActivityGrace();
                }
                _suspicionCount += 1;
                _state.ActiveTaskIndex = null;
                // Same-screen dwell: keep the clock running while the label is
                // unchanged; restart it the moment the screen changes.
                if (_offTaskStreakStart is null || !ActivityMatches(off.Label, _offTaskStreakActivity))
                {
                    _offTaskStreakStart = Now();
                    _offTaskStreakActivity = off.Label;
                }
                var dwell = (Now() - _offTaskStreakStart!.Value).TotalSeconds;
                if (dwell >= AppConstants.DriftConfirmSeconds && !InSettleWindow && _state.AppPhase == AppPhase.Session)
                {
                    LogCheck("offtask", justified: false, activity: off.Label,
                             excuse: "(drifted)", rule: "off-task", taskIndex: null);
                    _state.BallState = BallState.OffTask;
                    _state.AppPhase = AppPhase.OffTask;
                    _notifier.SendOffTaskNudge(_state.ActiveTasks.FirstOrDefault()?.Task ?? string.Empty);
                }
                break;

            case MultiTaskResult.Done done:
                CompleteTask(done.Index, done.Label);
                break;
        }
    }

    private void CreditTime(int index)
    {
        if (index >= 0 && index < _state.Tasks.Count)
            _state.Tasks[index].TimeOnTask += AppConstants.CycleSeconds;
    }

    /// Mark a task complete from a `.done` read. (The fire-and-forget
    /// summarizeCompletion is added in M2.5.)
    private void CompleteTask(int index, string label)
    {
        LastActivityLabel = label;
        Record(index, label);
        Stop();
        _state.CompleteTaskAt(index);
    }

    // MARK: - AMBIGUOUS resolution (ask once; accept/reject)

    /// The user said the ambiguous activity IS related. Take their word: create an
    /// allowance, log a justified kind="ambiguous" check, resume. Empty reason
    /// falls back to the activity label as the rule.
    public void AcceptAmbiguous(string reason)
    {
        var label = LastActivityLabel;
        var rule = string.IsNullOrWhiteSpace(reason) ? label : reason;
        int? taskIndex = null;
        var idx = _state.Tasks.FindIndex(t => !t.IsComplete);
        if (idx >= 0) taskIndex = idx;
        LogCheck("ambiguous", justified: true, activity: label, excuse: reason, rule: rule, taskIndex: taskIndex);
        if (taskIndex is { } ti) CreateAllowance(rule, ti);
        ResumeAfterExcuse();
    }

    /// The user said they drifted. Log a confirmed off-task drift, then show the
    /// OFF break/resume card.
    public void RejectAmbiguous()
    {
        LogCheck("offtask", justified: false, activity: LastActivityLabel,
                 excuse: "(drifted)", rule: "off-task", taskIndex: null);
        _state.BallState = BallState.OffTask;
        _state.AppPhase = AppPhase.OffTask;
    }

    private void CreateAllowance(string rule, int idx)
    {
        if (Store is not { } store || idx < 0 || idx >= _state.Tasks.Count) return;
        var normalized = TaskMatcher.Normalize(_state.Tasks[idx].Task);
        var existing = store.KnowledgeTasks.FirstOrDefault(k => k.NormalizedTitle == normalized);
        var kt = existing ?? new KnowledgeTask(normalized, DateTimeOffset.UtcNow);
        if (existing is null)
        {
            kt.OriginalTitles = new List<string> { _state.Tasks[idx].Task };
            store.AddKnowledgeTask(kt);
        }
        else if (!kt.OriginalTitles.Contains(_state.Tasks[idx].Task))
        {
            kt.OriginalTitles.Add(_state.Tasks[idx].Task);
        }
        kt.Allowances.Add(new Allowance(rule, DateTimeOffset.UtcNow));
        store.Save();
    }

    // MARK: - Excuse resolution (retained; not in the live 3-state flow)

    /// Evaluate a typed excuse. On a justified verdict, create an Allowance + link
    /// a KnowledgeTask. NOTE (as in the Swift original): the logged event keeps the
    /// default kind="offtask"; do not reintroduce this into the live flow without
    /// setting `kind` deliberately, or it would inflate the derived DriftCount.
    public async Task<ExcuseVerdict> HandleExcuseAsync(string text, IReadOnlyList<TaskItem> tasks, string screenText)
    {
        ExcuseVerdict verdict;
        try
        {
            verdict = await _ai.EvaluateExcuseAsync(text, tasks, screenText);
        }
        catch
        {
            return new ExcuseVerdict(false, null, string.Empty);
        }

        if (CurrentSession is { } session && Store is { } store)
        {
            session.Justifications.Add(new JustificationEvent(
                DateTimeOffset.UtcNow, text, verdict.Justified, verdict.TaskIndex,
                LastActivityLabel, verdict.Rule));   // kind defaults to "offtask"
            store.Save();
        }

        if (verdict.Justified && verdict.TaskIndex is { } taskIndex && taskIndex >= 0 && taskIndex < tasks.Count
            && Store is { } store2)
        {
            var taskTitle = tasks[taskIndex].Task;
            var normalized = TaskMatcher.Normalize(taskTitle);
            var existing = store2.KnowledgeTasks.FirstOrDefault(k => k.NormalizedTitle == normalized);
            var kt = existing ?? new KnowledgeTask(normalized, DateTimeOffset.UtcNow);
            if (existing is null)
            {
                kt.OriginalTitles = new List<string> { taskTitle };
                store2.AddKnowledgeTask(kt);
            }
            else if (!kt.OriginalTitles.Contains(taskTitle))
            {
                kt.OriginalTitles.Add(taskTitle);
            }
            kt.Allowances.Add(new Allowance(verdict.Rule, DateTimeOffset.UtcNow));
            store2.Save();
        }

        return verdict;
    }

    /// Active allowance rules keyed by task index (pending-confirmation ones excluded).
    public IReadOnlyDictionary<int, IReadOnlyList<string>> AllowanceRulesByIndex(IReadOnlyList<TaskItem> tasks)
    {
        var outp = new Dictionary<int, IReadOnlyList<string>>();
        if (Store is not { } store) return outp;
        for (int i = 0; i < tasks.Count; i++)
        {
            var normalized = TaskMatcher.Normalize(tasks[i].Task);
            var kt = store.KnowledgeTasks.FirstOrDefault(k => k.NormalizedTitle == normalized);
            if (kt is null) continue;
            var active = kt.Allowances.Where(a => !a.NeedsConfirmation).Select(a => a.Rule).ToList();
            if (active.Count > 0) outp[i] = active;
        }
        return outp;
    }
}
