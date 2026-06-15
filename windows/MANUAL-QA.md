# AccountaBall (Windows) — Manual QA Test Plan

_Last updated: 2026-06-12. Covers the WinUI 3 shell (M3 Platform + M4 App) as built._

The goal of this pass is to confirm the Windows port **feels and behaves like the
macOS app**: a calm always-on-top basketball that watches your screen every ~3s,
classifies what you're doing into 3 states, makes drift conscious without shaming,
and closes with a transparency-log recap. The macOS behavior spec is
[docs/HOW-IT-WORKS.md](../docs/HOW-IT-WORKS.md) — read §3–§9 first; each test below
cites the section it mirrors.

> **What is NOT done yet (don't fail the app for these).** Core milestones M2.5
> (session recap / `FinalizeSessionRecap`) and M2.6 (FreeBall recording engine) are
> not ported. So the **completion recap is a partial stub** (falls back to a task
> list when no `SessionRecap` is built) and **FreeBall does not actually record/
> summarize** yet — its cards are lifecycle stubs. These are tracked as known gaps
> (§9) and are expected to be thin until those milestones land. Everything else
> should match the Mac.

---

## 0. Environment & preconditions

| Need | How to satisfy |
|---|---|
| Windows 11 (10.0.22621+) | Build target; Win10 19045 dev box works to launch but Win11 is the support floor |
| **Ollama running + a model pulled** | `ollama serve` (or the tray app) and `ollama run qwen2.5:14b` once to pull. This box is configured for `qwen2.5:14b` in `%LOCALAPPDATA%\AccountaBall\config.json` |
| .NET 8 SDK | Pinned via `windows/global.json`; the .NET 10 SDK alone will not build the App (NETSDK1083) |
| A second app to "drift" to | A browser on YouTube / a news site — the obvious off-task screen |
| An on-task app | Whatever your declared task names (e.g. a Google Doc, VS Code) |

### Build & launch (must use the self-contained x64 exe)

```powershell
dotnet build windows\AccountaBall.App\AccountaBall.App.csproj -r win-x64 -p:Platform=x64
```

Launch the **self-contained** binary — the old `bin\Debug\...` output is stale and
un-hoverable, do not run it:

```
windows\AccountaBall.App\bin\x64\Debug\net8.0-windows10.0.22621.0\win-x64\AccountaBall.App.exe
```

### Parity gate (run before manual QA)

```powershell
dotnet test windows\AccountaBall.sln
```

Expected: **91/92 Core tests pass** (the 1 known failure is a pre-existing CRLF-vs-LF
assertion in `AiPromptsTests`, owned by the macOS side). If more than that fails,
stop — the engine logic regressed and manual QA is moot.

### Where to look when something's wrong

- App / crash log: `%LOCALAPPDATA%\AccountaBall\logs\app.log`
- DB: `%LOCALAPPDATA%\AccountaBall\accountaball.db` (SQLite — open with any viewer)
- Settings (tasks, drift limit): `%LOCALAPPDATA%\AccountaBall\settings.json`
- AI config (host/model): `%LOCALAPPDATA%\AccountaBall\config.json`

---

## 1. Launch & first-run (Welcome)

Mirrors HOW-IT-WORKS §3 (idle → welcome).

| # | Step | Expected |
|---|---|---|
| 1.1 | Launch the self-contained exe | A small floating card appears top-right of the work area, always-on-top, **not** in Alt-Tab and **not** on the taskbar switcher |
| 1.2 | Read the Welcome card | Title **"AccountaBall"**, body "Tell me what you're working on…", buttons **"Set up a session"** (primary) and **"Just observe (FreeBall)"** (secondary), and a **✕** top-right |
| 1.3 | Confirm the store initialized | `%LOCALAPPDATA%\AccountaBall\` exists with `accountaball.db` + `settings.json`; `logs\app.log` has no fatal lines |
| 1.4 | Hover the card; hover the buttons | Buttons show hover state — the panel is interactive (this was the M4.3a fix) |
| 1.5 | Click the **✕** | App exits cleanly (loop stops, process gone) |

**Parity note:** macOS bounce-in greeting + task pre-fill from last session. Windows
pre-fills saved tasks on next launch (test 8.x); there is no bounce animation.

---

## 2. Task setup & the pre-committed drift limit

Mirrors HOW-IT-WORKS §3 (setup) and §7 (drift engine). This is the load-bearing
accountability mechanism — test it carefully.

| # | Step | Expected |
|---|---|---|
| 2.1 | Welcome → **Set up a session** | Setup card: title "What are you working on?", a Task box, a Context box, **Add task**, a **Drift limit** stepper, and **Start watching** (disabled) |
| 2.2 | Type a task only (no context), click **Add task** | Task is added to the list with a **✕** remove button. (A task needs both fields filled to enable Start — see 2.4) |
| 2.3 | Add a fully filled task (task **and** context) | It appears in the list; **Start watching** becomes **enabled** |
| 2.4 | Add 2–3 more tasks | All listed with remove buttons; the macOS cap is 5 — confirm the UI stays usable |
| 2.5 | Remove a task via its **✕** | Row disappears; if no filled task remains, **Start watching** disables again |
| 2.6 | Drift-limit stepper | Default **3**; spin up/down clamps to **1–10**; you cannot go below 1 or above 10 |
| 2.7 | Caption under the stepper | Reads "How many confirmed drifts before you've broken your commitment." |
| 2.8 | Scroll the card | If content overflows, it scrolls (ScrollViewer) — no clipped buttons |

**Parity note:** the drift limit is editable **only here** (setup). Confirm there is
no way to change it mid-session — that immovability is the whole point (§7).

---

## 3. The monitoring loop — ON_TASK (silent, the most important "do nothing well")

Mirrors HOW-IT-WORKS §4 + §6 ON_TASK. **This exercises the open M3.3 risk: that WGC
capture actually yields a frame whose OCR feeds the classifier.**

| # | Step | Expected |
|---|---|---|
| 3.1 | Declare a task that matches an app you'll keep open (e.g. task "Write the QA doc", context "editing markdown in VS Code"), **Start watching** | Card collapses to the **compact round ball**, top-right. Ball shows the **on-task / happy** face once classified |
| 3.2 | Stay on the matching app for ~30s | Ball stays silent — **no card pops, no toast**. Doing nothing is correct behavior |
| 3.3 | Confirm capture+OCR+classify is live | In the DB, `TimelineEntry` rows accrue (one per on-task cycle, ~every 3s) with the activity label. If none accrue, capture/OCR is broken (see §10 debugging) |
| 3.4 | Tap the compact ball | Opens the **Progress** card (§7) — the ball is a status indicator; you act from the card |

**If 3.3 shows zero timeline entries**, this is the M3.3 unknown — capture or OCR is
not producing text. Check `app.log` and §10 before blaming classification.

---

## 4. AMBIGUOUS — ask once, take your word

Mirrors HOW-IT-WORKS §6 AMBIGUOUS. The classifier is **biased to prefer ON or
AMBIGUOUS over a false OFFTASK** — verified against `qwen2.5:14b` (Stack Overflow →
AMBIGUOUS, not OFFTASK).

| # | Step | Expected |
|---|---|---|
| 4.1 | While in a session, open a borderline work screen (docs/Stack Overflow related to your task but not literally it) | Within a cycle or two, the **Ambiguous** card appears: "Is this related to your task?", an optional reason box, **"Yes, it's related"** / **"No, I drifted"** |
| 4.2 | Click **Yes, it's related** (optionally type a reason) | Card dismisses; monitoring resumes; an **allowance** is created so this activity counts as on-task going forward. Ambiguous is **not** a drift — the off-counter resets |
| 4.3 | Return to the same ambiguous screen later | You are **not** asked again (`askedActivities` dedupe for the whole session) |
| 4.4 | Trigger a *different* ambiguous screen, click **No, I drifted** | Logged as a **confirmed drift**; the **OffTask** card follows (§5) |

**Parity note:** "Yes" takes your word — no AI re-judging. Verify the allowance holds
by staying on that screen and seeing the ball stay calm.

---

## 5. OFF_TASK — calm break / resume, drift on the *fact* not the button

Mirrors HOW-IT-WORKS §6 OFF_TASK + §8 (2-consecutive rule, settle window).

| # | Step | Expected |
|---|---|---|
| 5.1 | From a running session, switch to an obvious off-task screen (YouTube) and stay | After **2 consecutive** off-task reads (outside the 15s settle window), the ball goes **concerned/frown** and the **OffTask** card appears: "You've drifted off task", "Your task: {task}", buttons **Back to work** / **Take a 5-min break** / **It's actually related** |
| 5.2 | Confirm a single stray off-read does NOT prompt | Flick to YouTube for one cycle then back — no card (2-consecutive guard) |
| 5.3 | Click **Take a 5-min break** | Ball goes quiet; for 5 min there is **no prompting, no drift counting** even if you stay off-task (§8 timed break) |
| 5.4 | During a break, return to work | Time still credits; completion still honored; no prompt |
| 5.5 | Trigger the OffTask card again, click **Back to work** | Silent return to monitoring |
| 5.6 | Trigger it again, then just **return to a task** while the card is up | Card auto-dismisses; the self-return is logged and does **not** count against you |
| 5.7 | Trigger it again, then **ignore** the card for ~2 min | An **auto-return** is logged (honest record of the non-answer) and monitoring resumes |
| 5.8 | Click **It's actually related** | Resumes with a grace for the current activity (the dormant grace path) |

**Critical parity check:** the drift counts on the **fact** of being off-task, *not*
on which button you press. Pressing "Back to work" does not un-count the drift. Verify
via the recap (§6) or `JustificationEvent` rows with `kind="offtask"`.

---

## 6. Completion & the transparency-log recap

Mirrors HOW-IT-WORKS §9. **Partial stub — see §9 known gaps.** The recap renders from
a persisted `SessionRecap` *when present*; until Core M2.5 lands it may be null and
the card falls back to a plain task list.

| # | Step | Expected (today) | Expected (full parity, post-M2.5) |
|---|---|---|---|
| 6.1 | Get the model to read your task as complete (`DONE:N`) — e.g. close it out / show a "sent" state | Last task completes → **Completion** card, ball **beaming/Done** | same |
| 6.2 | Read the recap | "Session complete", "Total focus: N min", then either per-task commentary + drift line + ◐/●/○ check log (if a recap exists) or a bare ✓/• task list | Full recap: per-task AI commentary, "Drifts: N of LIMIT", and the check log every time |
| 6.3 | If you broke the limit (drifts ≥ limit) | Drift line includes "— commitment broken" | A firm red "✕ Commitment broken — you set a limit of L, you hit N" line |
| 6.4 | Check-log glyphs | ● confirmed drift · ◐ ambiguous clarified · ○ auto-return | same |
| 6.5 | Click **Done** | Resets: tasks cleared of completion, back to **Welcome**, ball Idle | same |

**Faithfulness check:** the recap is built from persisted records, so the drift count
in the recap must equal the number of `kind="offtask"` events you actually triggered
in §5 — they can't desync.

---

## 7. Progress card (tap-the-ball)

Mirrors HOW-IT-WORKS §3 (progress).

| # | Step | Expected |
|---|---|---|
| 7.1 | During a session, tap the compact ball | **Progress** card: "Progress", "Session: N min", and a line per task `• {task} — {m}m` (✓ when complete) |
| 7.2 | Click **Keep watching** | Returns to the compact ball, monitoring continues |
| 7.3 | Verify times advance | After more on-task cycles, the per-task minutes increase |

---

## 8. Returning session (persistence)

Mirrors HOW-IT-WORKS §3 (returning session pre-fill) + §10.

| # | Step | Expected |
|---|---|---|
| 8.1 | Complete or exit a session with tasks declared, relaunch the app | Welcome card; opening **Set up a session** shows the **previously declared tasks** pre-filled |
| 8.2 | Confirm the drift limit is remembered | The stepper shows the **last-used** limit (not reset to 3) |
| 8.3 | Inspect `settings.json` | Contains the declared tasks and the drift limit |

---

## 9. FreeBall (passive "just observe") — **stub until Core M2.6**

Mirrors HOW-IT-WORKS FreeBall design. Today the lifecycle/navigation works but **no
recording or summary is produced**. Test the flow, not the content.

| # | Step | Expected (today) |
|---|---|---|
| 9.1 | Welcome → **Just observe (FreeBall)** | Compact ball, **calm/Observing** face; capturing flag on |
| 9.2 | Tap the ball | **FreeBall Log** card: "Observing", body about quiet recording, **End & summarize** / **Past sessions** |
| 9.3 | Click **End & summarize** | **FreeBall Recap** card: "Where your time went", "Session summary ready." (stub text — no real summary yet), **Past sessions** / **Done** |
| 9.4 | Click **Past sessions** | **History** card: "Past sessions" + **Close** (stub list — empty) |
| 9.5 | Click **Done** / **Close** | Back to Welcome, ball Idle |

**Known gap:** real OCR recording, dedup, condense, markdown export, and the AI
summary all land with Core **M2.6**. Until then mid-session does zero AI by design
(matches Mac), but the recap is intentionally empty.

---

## 10. AI-unavailable handling & auto-recovery

Mirrors HOW-IT-WORKS §4 (skip-on-failure) + §11 + design §6.2.

| # | Step | Expected |
|---|---|---|
| 10.1 | **Stop Ollama** before launch, then launch | Startup probe fails → **"Can't reach the AI"** card with **Download Ollama** / **Retry** and the `ollama run qwen2.5:7b` hint |
| 10.2 | Click **Download Ollama** | Opens `https://ollama.com/download` in the default browser |
| 10.3 | Start Ollama, click **Retry** | Health check passes → card dismisses, app returns to its prior phase |
| 10.4 | Mid-session, **stop Ollama** | After a failed classify the app flips to **AiUnavailable** (the cycle is **skipped, never scored off-task**) |
| 10.5 | Restart Ollama, wait ~3s | Health polling **auto-recovers** without a click; monitoring resumes |
| 10.6 | Cancelled vs failed | Stopping the loop / quitting mid-request must not flip to AiUnavailable (a cancelled request is benign) |

---

## 11. Window behavior & "feel" (the calm-mirror polish)

Mirrors HOW-IT-WORKS §1 + macOS FloatingPanel parity.

| # | Step | Expected |
|---|---|---|
| 11.1 | Throughout, watch the panel | Always-on-top over other windows; anchored top-right of the work area with a margin |
| 11.2 | Off the switcher | Never in Alt-Tab, never on the taskbar |
| 11.3 | Per-phase sizing | The window resizes per phase (small round ball for Idle/Session/FreeBall; larger rounded cards otherwise). Cards aren't clipped; the ball is a clean circle (no square corners) |
| 11.4 | Card corner shape | Cards have rounded corners; the compact ball is a true circle (HWND region clip) |
| 11.5 | **Known deviation — focus steal** | Clicking the panel **activates it** (a known deviation: `WS_EX_NOACTIVATE` had to be dropped to keep hover/typing working). On Mac the ball never steals focus. **This is an accepted TODO, not a bug to file** — note whether it feels disruptive |
| 11.6 | Multi-monitor / DPI | Move the app's monitor or change scaling; the panel re-anchors and scales (DPI-aware). Spot-check at 100% and 150% |

---

## 12. Toasts (off-task nudges)

Mirrors HOW-IT-WORKS §2 (notifications) + INotifier.

| # | Step | Expected |
|---|---|---|
| 12.1 | On a confirmed drift (§5.1), watch the Action Center | A toast nudge fires. **Note:** unpackaged apps may fail to register the toast COM activator — this is non-fatal by design (`Register()` is guarded). If no toast appears, confirm `app.log` shows the non-fatal register message rather than a crash |

---

## Summary scorecard

Fill this in during the pass:

| Area | Pass / Fail / Partial | Notes |
|---|---|---|
| 1. Launch & Welcome | | |
| 2. Setup & drift limit | | |
| 3. ON_TASK loop (capture→OCR→classify) | | ← the M3.3 real-capture validation |
| 4. AMBIGUOUS ask-once | | |
| 5. OFF_TASK break/resume + drift-on-fact | | |
| 6. Completion recap | | partial until M2.5 |
| 7. Progress card | | |
| 8. Returning-session persistence | | |
| 9. FreeBall | | stub until M2.6 |
| 10. AI unavailable + recovery | | |
| 11. Window feel (focus-steal deviation) | | |
| 12. Toasts | | best-effort unpackaged |

**Top parity risks to watch:** (a) does WGC capture actually feed the classifier
(§3); (b) does drift count on the fact not the button (§5); (c) does the recap stay
faithful to the persisted drift count (§6); (d) the focus-steal deviation (§11.5).
