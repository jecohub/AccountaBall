using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AccountaBall.Core.Models;
using AccountaBall.Core.Services;
using AccountaBall.Core.Storage;
using AccountaBall.Core.Util;

namespace AccountaBall.Core.Engine;

/// Passive observation mode. Captures full screen text every cycle, dedups
/// consecutive near-identical reads, stores everything, and runs NO AI until
/// EndAsync() — which assembles the transcript + past recaps and makes one
/// summarizeFreeBall call. Faithful port of Swift `FreeBallEngine`.
///
/// The live capture loop (`begin`) and the macOS App-Nap token are Platform/App
/// concerns; this Core engine exposes <see cref="BeginForTest"/> (the session-record
/// seam), <see cref="Ingest"/> (called per cycle by the Platform capture loop), and
/// <see cref="EndAsync"/>.
public sealed class FreeBallEngine
{
    private readonly AppState _state;
    private readonly IAiService _ai;

    public IStore? Store { get; set; }
    public Func<DateTimeOffset> Now { get; set; } = () => DateTimeOffset.UtcNow;
    public FreeBallSession? CurrentSession { get; private set; }

    public FreeBallEngine(AppState state, IAiService ai)
    {
        _state = state;
        _ai = ai;
    }

    /// Begin a session record and switch to the FreeBall phase. The Platform/App
    /// layer calls this, then starts its capture loop and drives <see cref="Ingest"/>
    /// per cycle (the capture loop itself is not part of Core).
    public void Begin() => BeginSessionRecord();

    private void BeginSessionRecord()
    {
        if (Store is not { } store) return;
        var session = new FreeBallSession(Now());
        store.AddFreeBallSession(session);
        store.Save();
        CurrentSession = session;
        _state.FreeBallStartTime = session.StartedAt;
        _state.FreeBallRecap = null;
        _state.AppPhase = AppPhase.FreeBall;
    }

    /// Ingest one OCR read: extend the last block if it's the same screen, else open
    /// a new block. Never calls the AI.
    public void Ingest(string text)
    {
        if (CurrentSession is not { } session || Store is not { } store) return;
        session.CycleCount += 1;
        var last = session.Captures.Count == 0
            ? null
            : session.Captures.Aggregate((a, b) => a.LastSeenAt >= b.LastSeenAt ? a : b);
        if (last is not null && FreeBallDedup.IsSameScreen(last.Text, text))
        {
            last.LastSeenAt = Now();
        }
        else
        {
            session.Captures.Add(new FreeBallCapture(Now(), Now(), text));
        }
        store.Save();
    }

    /// End the session: stop capturing (Platform), show the recap with a loading
    /// state, then make the single AI summarize call. Degrades gracefully if the AI
    /// is down.
    public async Task EndAsync()
    {
        if (CurrentSession is not { } session || Store is not { } store) return;
        session.EndedAt = Now();
        var duration = (session.EndedAt!.Value - session.StartedAt).TotalSeconds;

        _state.FreeBallSummarizing = true;
        _state.AppPhase = AppPhase.FreeBallRecap;

        var entries = session.Captures
            .OrderBy(c => c.FirstSeenAt)
            .Select(c => new FreeBallTranscriptEntry(c.Text, Math.Max(c.Seconds, AppConstants.CycleSeconds)))
            .ToList();
        var totalChars = entries.Sum(e => e.Text.Length);

        // Trivial session: skip the AI, show a gentle recap.
        if (totalChars < AppConstants.FreeBallMinCharsToSummarize)
        {
            session.Narrative = "Not enough captured to summarize yet.";
            store.Save();
            PublishRecap(session.StartedAt, duration, new FreeBallSummary(
                session.Narrative, Array.Empty<CategorySpan>(), "",
                Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>()), pending: false);
            FinishEnd();
            return;
        }

        var condensed = FreeBallCondenser.Condense(entries);
        var pastRecaps = FetchPastRecaps(session);

        try
        {
            var summary = await _ai.SummarizeFreeBallAsync(condensed, pastRecaps);
            session.Narrative = summary.Narrative;
            session.Categories = summary.Categories.ToList();
            session.Insight = summary.Insight;
            session.WorkingOn = summary.WorkingOn.ToList();
            session.People = summary.People.ToList();
            session.CodeContext = summary.CodeContext.ToList();
            session.OpenThreads = summary.OpenThreads.ToList();
            session.RecapPending = false;
            store.Save();
            PublishRecap(session.StartedAt, duration, summary, pending: false);
        }
        catch
        {
            session.RecapPending = true;   // keep raw captures for a later pass
            store.Save();
            _state.SetupHint = "Couldn't summarize this FreeBall session — the AI was unreachable. Your capture was saved.";
            PublishRecap(session.StartedAt, duration, new FreeBallSummary(
                "", Array.Empty<CategorySpan>(), "",
                Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>()), pending: true);
        }
        FinishEnd();
    }

    private void FinishEnd()
    {
        _state.FreeBallSummarizing = false;
        CurrentSession = null;
    }

    private void PublishRecap(DateTimeOffset date, double duration, FreeBallSummary s, bool pending)
    {
        _state.FreeBallRecap = new FreeBallRecap(
            date, duration, s.Narrative, s.Categories, s.Insight,
            s.WorkingOn, s.People, s.CodeContext, s.OpenThreads, pending);
    }

    /// Most recent completed recaps (excluding this session), capped, as cross-session context.
    private IReadOnlyList<FreeBallPastRecap> FetchPastRecaps(FreeBallSession session)
    {
        if (Store is not { } store) return Array.Empty<FreeBallPastRecap>();
        return store.FreeBallSessions
            .Where(s => s.Id != session.Id && s.EndedAt != null && !s.RecapPending && s.Narrative.Length > 0)
            .OrderByDescending(s => s.EndedAt ?? DateTimeOffset.MinValue)
            .Take(AppConstants.FreeBallPastRecapCap)
            .Select(s => new FreeBallPastRecap(s.Narrative, s.Categories, s.Insight, s.OpenThreads))
            .ToList();
    }
}
