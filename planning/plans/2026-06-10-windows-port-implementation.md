# AccountaBall Windows Port — Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Build a native Windows app at full parity with the macOS app's Phase-1
accountability spine + FreeBall, as a separate C#/.NET codebase.

**Architecture:** Brain/shell split. A portable `AccountaBall.Core` (engine,
models, prompts — zero `Windows.*` deps) is a faithful C# translation of the Swift
brain and is proven by the ported test suite. The OS-native shell
(`AccountaBall.Platform` + `AccountaBall.App`) implements `IScreenCapture`,
`IOcrService`, `INotifier`, `IStore` and the WinUI 3 UI. Parity = ported tests
pass.

**Tech Stack:** C#/.NET 8, WinUI 3 (Windows App SDK), `Windows.Graphics.Capture`,
`Windows.Media.Ocr`, EF Core + SQLite, xUnit. Windows 11 minimum.

**Source of truth:** the macOS Swift source under
`src/Sources/AccountaBall/` and the design doc
`planning/plans/2026-06-10-windows-port-design.md`. Where a task says "port
`X.swift`", that Swift file is the spec — read it, translate behavior exactly
(same cadence, same windows, same derived values, **prompts verbatim**).

> **NOTE for a fresh session (esp. on Windows):** the Swift `src/` is NOT in this
> repo — it lives in a separate private repo `jecohub/AccountaBall-macOS`. The
> remaining Core ports (M2.4–2.6) need that Swift source as their spec, so do them
> on a machine that has it cloned. The Windows-only milestones (M3 Platform, M4
> WinUI shell) do NOT need the Swift source.

---

## ✅ BUILD PROGRESS (last updated 2026-06-12)

The .NET solution lives in `windows/` (classic `.sln`). Core/Tests target `net8.0`;
Platform + App target `net8.0-windows10.0.22621.0`. **The whole solution builds
win-x64 (0 warnings / 0 errors) and 94/95 Core tests pass** (the 1 failure is a
pre-existing CRLF-vs-LF assertion in `AiPromptsTests`, left to the macOS side per
the repo owner — Core is treated read-only on Windows). The +3 over the prior
91/92 are the M2.5 `EngineSessionRecapTests` added below.

### Session log — 2026-06-12 (capture root-cause fix + M2.5 recap)
Triggered by a live test where a declared session **never prompted** (LinkedIn
ignored) and the recap was empty. Two distinct problems, both now addressed:

1. **The monitoring loop was failing silently AND invisibly.** `AppController`'s
   capture tick swallowed every early-return/exception with a bare `catch {}` and
   logged nothing, so there was no `app.log` to diagnose from. Added per-cycle
   diagnostics (`App.Log`) for each stage: capture-null / OCR-empty / `ocrChars=…` /
   `classify=… -> phase/ball` / classify-failure / `tick error: …`, plus
   `controller started` and `session started` markers.
2. **Root cause of "no prompt" = WGC/D3D interop (the open M3.3 risk) threw every
   cycle**, so the engine never received a single frame (hence no prompts, 0 min
   focus). The log showed two throws:
   - `Direct3D11CaptureFramePool.CreateFreeThreaded` → `InvalidCastException: Failed
     to create a CCW … IID 'A37624AB-…'` (WinRT `IDirect3DDevice`). Cause:
     `Direct3D11Interop.CreateDevice` projected the device via legacy
     `Marshal.GetObjectForIUnknown`, yielding a `__ComObject` that WindowsAppSDK's
     CsWinRT can't marshal back as `IDirect3DDevice`. **Fix:** project through
     `WinRT.MarshalInspectable<IDirect3DDevice>.FromAbi`.
   - `IGraphicsCaptureItemInterop.CreateForWindow` → `ArgumentException` for a
     non-capturable foreground window (shell/secure/elevated/zero-size). **Fix:**
     `EnsureSessionForWindow` now returns `bool` and try/catches the build; a
     non-capturable window skips the cycle instead of crashing the loop.
   ⚠️ **Still needs the runtime confirmation pass** (the original M3.3 unknown):
   that a real session now logs `ocrChars=… / classify=OffTask` and surfaces the
   OffTask card on a sustained drift. Code builds clean; not yet human-verified.

3. **M2.5 `FinalizeSessionRecapAsync` ported** (see "Next up" below) — the empty
   recap is fixed: the Completion card now renders timeline + per-task commentary
   (AI, local "faster/slower than last time" fallback) + `Drifts: N of L` + the
   ◐/●/○ check log. Spec came from in-repo `planning/plans/2026-06-04-…-impl.md`
   Task 10 + the 3-state design §3 — **no macOS clone needed.** `SummarizeCompletion`
   (persist a `TaskCompletion` for cross-session comparison) and `ProposeMatch`
   remain deferred.

### Session log — 2026-06-12 (capture frame-delivery + STA-dispose fixes)
After the device-CCW + `CreateForWindow` fixes above, a fresh live test **still
never prompted** (LinkedIn ignored the whole session) and the recap was again
empty (`Total focus: 1 min` = wall-clock only, 0 drifts). `app.log` showed every
cycle logging `capture returned null (no frame)` with steady ~6 s gaps == the
safety timeout. Two more interop bugs, both fixed (in `GraphicsCaptureService`):

4. **WGC frame delivery on static windows.** The frame pool only raises
   `FrameArrived` when the captured window composes a **new** frame. A still page
   (LinkedIn sitting there) composes nothing, so "await the next FrameArrived" on a
   long-lived per-HWND session hung until the 6 s timeout → null every cycle. The
   one frame WGC *guarantees* is the initial one right after `StartCapture()`, but
   the old code attached its handler **after** start, so it was always missed.
   **Fix:** switched to the standard single-shot screenshot recipe — build a fresh
   pool+session each cycle, attach `FrameArrived` **before** `StartCapture()`, grab
   that guaranteed first frame, then tear the session down. The D3D device is the
   one expensive object and is kept alive across calls. (Dropped the per-HWND
   `EnsureSessionForWindow`/`TearDownSession` caching — recreate-per-cycle, which
   also matches the macOS "one frame per cycle" model.)
5. **STA wrong-thread dispose.** With #4 in place capture finally produced frames,
   but the cycle then threw `COMException 0x8001010E ("interface marshalled for a
   different thread")` at `GraphicsCaptureSession.Dispose()` — losing the frame.
   The session/item are created on (and STA-bound to) the UI-thread tick, but the
   `await … .ConfigureAwait(false)` resumed the `finally` (the `Dispose()` calls) on
   an MTA pool thread. **Fix:** dropped `ConfigureAwait(false)` so disposal resumes
   on the creating UI thread. (The free-threaded frame pool is agile; only the
   session/item are apartment-bound.)
   ✅ **HUMAN-VERIFIED 2026-06-12 21:51** — live session on LinkedIn (Brave) logged
   real frames (`ocrChars=1024…1918`), `classify=OffTask` per cycle, and after the
   15 s same-screen dwell flipped `phase=Session → phase=OffTask ball=OffTask` and
   surfaced the OffTask card. Full pipeline confirmed: WGC capture → OCR → Ollama
   classify → engine drift confirm → prompt. (Note observed in testing: the dwell
   timer restarts on any window switch, so a drift only confirms after ~15 s of
   *continuous* off-task screen — earlier "no prompt" runs were the user alt-tabbing
   between LinkedIn and VS Code, resetting the streak each time.)

### Session log — 2026-06-15 (no-focus-steal, draggable panel, combined history)
Three M4.5 follow-ups (App/Platform only; builds win-x64 0/0):

1. **No-focus-steal for the passive ball (the open M4.5 TODO) — RESOLVED + verified.**
   The earlier-abandoned `WS_EX_NOACTIVATE` turned out to be the right tool: the prior
   "it kills island input" conclusion was **wrong**. Empirically (automated test below),
   `WS_EX_NOACTIVATE` blocks foreground *activation* but does **not** block the WinUI
   island from receiving the click — the ball stays fully tappable. (Dead ends ruled out
   first, all confirmed not to work via the test harness: a top-level
   `WM_MOUSEACTIVATE`→`MA_NOACTIVATE` subclass; additionally subclassing the child island
   `Microsoft.UI.Content.DesktopChildSiteBridge`; and a deferred `WM_ACTIVATE` focus
   bounce-back via `AttachThreadInput`+`SetForegroundWindow` — WinUI activates on
   pointer-press bypassing `WM_MOUSEACTIVATE`, and cross-process foreground hand-back is
   blocked by the foreground lock / re-grabbed when the tap opens a card.)
   **Final impl:** `NativeWindow.SetNoActivate(hwnd, on)` toggles `WS_EX_NOACTIVATE`;
   `FloatingPanel.ResizeFor` calls it per phase — **on for the compact ball**
   (Idle/Session/FreeBall), **off for cards** (Setup text box needs keyboard focus;
   cards are unchanged from the previously-working state — they never had the style).
   ✅ **VERIFIED 2026-06-15** by an automated harness (UIA-drive into FreeBall →
   ball; Notepad as the foreground reference; synthesized click on the ball): foreground
   **stayed on Notepad**, the **tap still opened the FreeBall card**, and the card's
   ex-style had `NOACTIVATE` cleared. (Human spot-check of Setup typing still advisable,
   but cards are behaviorally identical to before this change.)
2. **Draggable panel that stays put.** New `Shell/WindowDrag.cs` attaches to the ball
   and `CardHost`: defers pointer capture until the cursor moves past a 4px threshold
   (so a plain click still fires the ball Tapped / card buttons), then moves the window
   to `GetCursorPos() - grabOffset` (absolute-cursor math, no moving-frame feedback).
   `FloatingPanel` gained `Position`/`MoveTo` (work-area clamped) and a `_userMoved`
   flag — once the user drags, `ResizeFor` **stops re-anchoring to top-right** so the
   panel keeps its position across phase changes (cards grow from the current top-left).
3. **Combined history moved to the Welcome card.** Welcome now has a **"History"**
   button (→ existing `AppPhase.FreeBallHistory`, since `AppPhase` is read-only Core).
   `IShellActions.History()` (replacing `FreeBallHistory()`) merges declared **Session**
   + **FreeBall** runs newest-first as `HistoryEntry(Type, Date, Summary, Pending)`.
   FreeBall summary = `Narrative`; Session summary is **derived** from the persisted
   `WorkSession` (`SqliteStore.Sessions` getter, Platform-only) as
   `"{tasks} · {min} min · {k} off-task"` (no narrative is persisted for declared
   sessions). `FreeBallCardView`'s history case renders `[Type] · date` + summary.

### Session log — 2026-06-15 (FreeBall wired end-to-end; Core M2.5/M2.6 pulled)
The Mac pushed the rest of Core to `origin/master`: `ab96886` (M2.6 FreeBall engine
+ models + utils), `e453157` (M2.5 completion/recap/match — "Core complete"), on top
of `978c618` (the continuous-dwell fix). **Reconciled without losing the uncommitted
Windows capture fixes**: `git checkout origin/master -- windows/AccountaBall.Core
windows/AccountaBall.Core.Tests` (Core is read-only here; a `git reset --hard` would
have wiped the unpushed `GraphicsCaptureService`/`Direct3D11Interop` work). Dropped the
now-redundant local `EngineSessionRecapTests.cs` (master ships
`EngineCompletionRecapMatchTests.cs`). Core now 112/113 green (the 1 is the known
pre-existing CRLF `AiPromptsTests` case).

Then built the **in-scope Platform + App glue** for FreeBall:
- **M3 Platform:** `OllamaAiService.SummarizeFreeBallAsync` (builds via
  `AiPrompts.BuildFreeBallPrompt`, system = `AiPrompts.FreeBallSystem`, parses via the
  tolerant `AiPrompts.ParseFreeBallSummary` — refactored the HTTP helper to expose the
  raw response string). `SqliteStore.AddFreeBallSession`/`FreeBallSessions` +
  `AccountaBallDbContext` mapping: `FreeBallSession` (real Guid key; `WorkingOn`/`People`/
  `CodeContext`/`OpenThreads` as EF8 primitive collections; `Categories` via
  `OwnsMany(...).ToJson()`; `Captures` as a cascade child table; `FreeBallCapture.Seconds`
  ignored — computed).
- **M4 App:** `AppController` now owns a `FreeBallEngine`; `StartFreeBall` → `Begin()`,
  a new `FreeBallTickAsync` (capture→OCR→`Ingest`, **no per-cycle AI**) runs while
  `_freeBall.CurrentSession` is live, `EndFreeBall` → `EndAsync()` (renders the
  "Summarizing…" state, then the result). `FreeBallCardView` renders the real recap
  (narrative + categorized minutes + insight + working-on/people/code/open-threads) and
  a history browser via the new `IShellActions.FreeBallHistory()` (reads
  `Store.FreeBallSessions`).
- ⚠️ **Schema-change gotcha:** the store uses `EnsureCreated()` (no migrations), so the
  new FreeBall tables don't appear in a pre-existing `accountaball.db`. **Deleting the
  dev DB** at `%LOCALAPPDATA%\AccountaBall\` is required after a Core schema change; did
  so — app relaunched clean (EF model valid, DB rebuilt). **FreeBall end-to-end
  (observe→ingest→summarize→recap) not yet human-verified** — next.

> **Windows build note:** the machine has .NET SDK 8.0.422 **and** 10.0.301. The
> port is pinned to the 8.x SDK via `windows/global.json` because the .NET 10 SDK
> rejects WindowsAppSDK 1.5's legacy `win10-*` RIDs (NETSDK1083). Build/run the App
> with an explicit RID **and platform** (self-contained mode rejects `AnyCPU`):
> `dotnet build windows\AccountaBall.App\AccountaBall.App.csproj -r win-x64 -p:Platform=x64`.
> The exe lands at
> `windows\AccountaBall.App\bin\x64\Debug\net8.0-windows10.0.22621.0\win-x64\AccountaBall.App.exe`.
> `dotnet test windows\AccountaBall.sln` still works as the parity gate — it only
> builds Core + Core.Tests (App/Platform aren't test deps), so the App's
> self-contained settings don't affect it.

### M3 — Platform integrations (`AccountaBall.Platform`) — DONE (builds clean)
- [x] **M3.1** — `IScreenCapture` / `IOcrService` interfaces. **Kept in Platform,
      not Core** (the engine never references frames), so Core stays untouched.
- [x] **M3.2** — `WindowsMediaOcrService` (`Windows.Media.Ocr`, Bgra8 convert).
- [x] **M3.3** — `GraphicsCaptureService` + `Direct3D11Interop` (WGC single-shot
      frame per cycle: fresh pool+session each tick, handler before `StartCapture()`).
      ⚠️ **Four interop bugs found + fixed on 2026-06-12** (device CCW projection via
      `MarshalInspectable.FromAbi`; `CreateForWindow` ArgumentException caught → skip
      cycle; static-window frame-delivery → single-shot recipe; STA wrong-thread
      `Dispose` → dropped `ConfigureAwait(false)`). See the two session logs above.
      Builds clean; **still needs the real-capture validation pass** (frame actually
      yields OCR text → classify=OffTask) — the open runtime unknown.
- [x] **M3.4** — `SqliteStore` + `AccountaBallDbContext` (EF Core/SQLite). Shadow
      keys + EF8 primitive collections so the Core POCOs keep zero EF annotations.
      Schema via `EnsureCreated()` (no migrations yet). Also `FileKeyValueStore`
      (JSON at `%LOCALAPPDATA%\AccountaBall\settings.json`) for the UserDefaults analog.
- [x] **M3.5** — `ToastNotifier` (`AppNotificationManager`). **Moved into the App
      project** — WindowsAppSDK drags in the `win10-*` RIDs, so the Platform library
      stays WindowsAppSDK-free and the packaged App owns Register()/AUMID.
- [x] **M3.6** — `OllamaAiService` + `OllamaConfig` (all 6 `IAiService` methods,
      `AiPrompts` verbatim, `format:json` + temp 0, env → config.json → defaults).

### M4 — WinUI 3 shell (`AccountaBall.App`, unpackaged) — IN PROGRESS
- [x] **M4.0** — project scaffold, `app.manifest` (PerMonitorV2). Launches.
- [x] **M4.1** — `FloatingPanel`: borderless, always-on-top, off-switcher
      (`WS_EX_TOOLWINDOW`), DPI-aware per-`AppPhase` resize. **Deviation from macOS
      parity:** `WS_EX_NOACTIVATE` was REMOVED — on a WinUI 3 window it suppresses
      hover/click input to the XAML island (the ball/cards became un-hoverable and the
      Setup text box couldn't be focused to type). The panel is now a normal activatable
      floating tool window: clicking it focuses it. A `WM_MOUSEACTIVATE`/`MA_NOACTIVATE`
      subclass was tried but does NOT stop the island's input bridge from activating, so
      strict no-focus-steal for the passive ball remains a TODO (would need the bridge
      subclassed).
- [x] **M4.2** — `BallView` (basketball, per-`BallState` faces incl. Observing);
      round/rounded HWND clip via `SetWindowRgn`. Launch-verified.
- [x] **M4.3** — `RootCoordinator` + code-first phase cards (`IPhaseView`/
      `IShellActions`): Welcome, TaskSetup (drift stepper), WhatsUp, Ambiguous,
      OffTask, Progress, Completion (recap + ◐/●/○ log), AiUnavailable, FreeBall
      card. `UiKit` helper. FreeBall recap/history are functional stubs pending Core M2.6.
- [x] **M4.3a** — interaction affordances added during the launch-QA pass (all
      verified live: hover pixel-diff, real-click, UIA):
  - **Hover/click input fix.** The panel was un-interactive (no hover, Setup text box
    couldn't be focused) because `WS_EX_NOACTIVATE` suppresses pointer input to the
    WinUI 3 island — removed (see M4.1). Hover + clicks now work.
  - **Quit (X) button.** A Windows-style ✕ top-right of every card phase →
    `IShellActions.ExitApp` (stops the loop, `Application.Exit`).
  - **Ball is tappable.** The bare ball (FreeBall/Session) had no handler and the X is
    hidden there, so a session couldn't be ended — `Ball.Tapped` → `BallTapped`:
    FreeBall → `FreeBallLog` (End & summarize / Past sessions), Session → `Progress`.
  - **Stale-build gotcha:** only the self-contained `bin\x64\Debug\...\win-x64\` exe is
    valid; the old `bin\Debug\...` output was deleted (running it gave the pre-fix,
    un-hoverable binary). Always launch the `x64` path.
- [x] **M4.4** — `AppController` wires store/providers/engine + the ~3s capture loop
      (DispatcherQueueTimer, all engine/EF access on the UI thread), implements
      `IShellActions`, and `App.OnLaunched` registers toasts + starts it. **Launch
      verified:** the Welcome card renders (Set up a session / Just observe), the
      ball shows, and the store writes `%LOCALAPPDATA%\AccountaBall\accountaball.db`
      + `settings.json`. **Root cause of the earlier "no dir/log" hang:** the
      unpackaged WindowsAppSDK bootstrapper couldn't find the Windows App Runtime,
      so the generated `Main` threw in `XamlCheckProcessRequirements()`/bootstrap
      **before** `OnLaunched` (hence no log). **Fix:** bundle the runtime via
      `WindowsAppSDKSelfContained` + `SelfContained` in the csproj — which also
      requires building with an explicit `-p:Platform=x64` (self-contained rejects
      `AnyCPU`). The crash logger + non-fatal `Register()` guard are kept.
- [~] **M4.5** — manual "feel it" QA pass. **AI half proven (automated):** this
      machine runs **`qwen2.5:14b`** (set in `%LOCALAPPDATA%\AccountaBall\config.json`;
      ~9.5 GB, 100% GPU-resident on the 12 GB RTX 3060, classify 1–4 s — inside the 3 s
      cadence). Validated the live `ClassifySystem` contract end-to-end: the model emits
      the expected `{"result","label"}` JSON and the ON / AMBIGUOUS / OFFTASK bias holds
      (Stack Overflow → AMBIGUOUS, not OFFTASK). **Still needs a human at the keyboard:**
      (1) **WGC real-capture validation** — confirm `GraphicsCaptureService` actually
      yields a frame whose OCR text feeds the classifier (the M3.3 open unknown; only
      exercised once a session is running); (2) ✅ **strict no-focus-steal — DONE**
      (2026-06-15 session log above): `WS_EX_NOACTIVATE` toggled per-phase (on for the
      passive ball, off for cards) keeps the foreground app focused on a ball click while
      the ball stays tappable — verified by an automated harness. The old "it kills island
      input" claim was wrong; (3) cards size right per phase; (4) toasts fire on a confirmed
      drift; (5) the full on→ambiguous→off→break→complete loop + a FreeBall record/recap
      feel like the Mac.

### Earlier (Core, done on macOS — read-only on Windows)
**92 unit tests green, all committed:**

- [x] **M0** — solution scaffold (`AccountaBall.Core` + `AccountaBall.Core.Tests`).
- [x] **M1** — `AppConstants`, `AppPhase`/`BallState`, `MultiTaskResult` + parse,
      `AiPrompts` (6 system prompts verbatim).
- [x] **M2.1 / M2.2** — `TaskItem`, `TaskSession`; `TaskMatcher`, `DurationDelta`,
      `TimelineCoalescer` + the `SessionRecap` value types (`TimelineRange`,
      `PerTaskComment`, `CheckLogItem`, `SessionRecap`, `PerTaskSessionInput`).
- [x] **M2.3** — `AppState` (UI binding deferred to App layer) + `IKeyValueStore`
      (in-memory fake; real impl is Platform/M3).
- [x] **M2.4** — `AccountabilityEngine`. Persistence POCOs (`WorkSession`,
      `TimelineEntry`, `JustificationEvent`, `KnowledgeTask`, `Allowance`,
      `TaskCompletion`) behind `IStore` (+ `InMemoryStore`); `IAiService` (+
      `TaskPreviousRun`), `INotifier`, `ExcuseVerdict` (+ parse), `TaskRecap`. The
      decision logic: `ProcessResult`/`ProcessCycle`, lifecycle, derived
      `DriftCount`/`CommitmentBroken`, 2-consecutive + dwell drift confirm, AMBIGUOUS
      ask-once, timed break, settle + activity-grace windows, auto-return, AI
      pause/recover, allowances + `HandleExcuseAsync`. 41 engine tests (6 files).
      Dropped (macOS-only): the App-Nap activity token.

**Next up:**
- [~] **M2.5** — completion + recap + match. **DONE 2026-06-12:**
      `FinalizeSessionRecapAsync` (recap + transparency log) ported + wired into the
      `DONE` path and manual end in `AppController` (`FinalizeAndCloseAsync`, guarded
      by `_recapFinalized`); `EngineSessionRecapTests` added (3 tests, green).
      **Still deferred:** `SummarizeCompletionAsync` (KnowledgeTask/TaskCompletion —
      without it the recap's "first time finishing" never advances to a real
      faster/slower comparison across sessions), `ProposeMatchAsync` (cheap + AI
      match), the allowance-confirm-on-reuse queue. Port `EngineCompletionTests`,
      `EngineMatchTests` when those land.
- [ ] **M2.6** — FreeBall engine + condenser/dedup/markdown/export + add
      `summarizeFreeBall` to `IAiService`.
- [ ] Also deferred from M2.1/2.2: `ScreenText` + `Snapshots` (need the OCR /
      persistence abstractions).
- [ ] **M3 / M4** — Platform integrations + WinUI shell (Windows 11 machine).

**Build/test commands.** On macOS the Homebrew dotnet needs its env:
```bash
export DOTNET_ROOT="/opt/homebrew/opt/dotnet/libexec"
export PATH="/opt/homebrew/opt/dotnet/bin:$PATH"
dotnet test windows/AccountaBall.sln
```
On Windows with the .NET 8 (or newer) SDK installed, just:
`dotnet test windows\AccountaBall.sln`. The test project sets
`RollForward=LatestMajor`, so a newer-only runtime still runs the net8.0 tests.

---

## Milestone 0 — Solution scaffold

### Task 0.1: Create the solution and projects

**Files:**
- Create: `windows/AccountaBall.sln`
- Create: `windows/AccountaBall.Core/AccountaBall.Core.csproj` (net8.0, library)
- Create: `windows/AccountaBall.Core.Tests/AccountaBall.Core.Tests.csproj` (net8.0, xUnit)
- Create: `windows/AccountaBall.Platform/AccountaBall.Platform.csproj` (net8.0-windows10.0.22621.0)
- Create: `windows/AccountaBall.App/AccountaBall.App.csproj` (WinUI 3, net8.0-windows10.0.22621.0)

**Step 1:** Scaffold with the .NET CLI:
```bash
cd windows
dotnet new sln -n AccountaBall
dotnet new classlib -n AccountaBall.Core -f net8.0
dotnet new xunit   -n AccountaBall.Core.Tests -f net8.0
dotnet sln add AccountaBall.Core/AccountaBall.Core.csproj AccountaBall.Core.Tests/AccountaBall.Core.Tests.csproj
dotnet add AccountaBall.Core.Tests reference AccountaBall.Core
```
(`.Platform` and `.App` are added in their milestones — they need the Windows App
SDK workload and only build on Windows.)

**Step 2:** Verify the solution builds.
Run: `dotnet build windows/AccountaBall.sln`
Expected: build succeeds, 0 warnings.

**Step 3: Commit**
```bash
git add windows/
git commit -m "chore(win): scaffold AccountaBall.Core + test solution"
```

---

## Milestone 1 — Core foundation (pure logic, full TDD)

These translate Swift files I have exact source for. Code below is complete.

### Task 1.1: AppConstants

**Files:**
- Create: `windows/AccountaBall.Core/Util/AppConstants.cs`
- Test: `windows/AccountaBall.Core.Tests/AppConstantsTests.cs`

**Step 1: Write the failing test**
```csharp
using AccountaBall.Core.Util;
public class AppConstantsTests {
    [Fact] public void CadenceAndWindows_MatchMacOS() {
        Assert.Equal(3, AppConstants.CycleSeconds);
        Assert.Equal(15, AppConstants.DriftConfirmSeconds);
        Assert.Equal(5 * 60, AppConstants.BreakSeconds);
        Assert.Equal(0.85, AppConstants.FreeBallDedupThreshold, 3);
    }
}
```
**Step 2:** Run: `dotnet test --filter AppConstantsTests` → FAIL (type missing).

**Step 3: Implement** (port `Util/AppConstants.swift` verbatim; seconds as `int`,
threshold as `double`):
```csharp
namespace AccountaBall.Core.Util;
public static class AppConstants {
    public const int CycleSeconds = 3;
    public const int PeripheralScreenChars = 500;
    public const int ContinueAnywayGraceSeconds = 120;
    public const int BreakSeconds = 5 * 60;
    public const int DriftConfirmSeconds = 15;
    public const double FreeBallDedupThreshold = 0.85;
    public const int FreeBallMaxTranscriptChars = 28000;
    public const int FreeBallMinCharsToSummarize = 40;
    public const int FreeBallPastRecapCap = 10;
    public const int FreeBallSummarizeTimeout = 120;
}
```
**Step 4:** Run: `dotnet test --filter AppConstantsTests` → PASS.

**Step 5: Commit**
```bash
git add windows/AccountaBall.Core/Util/AppConstants.cs windows/AccountaBall.Core.Tests/AppConstantsTests.cs
git commit -m "feat(win): port AppConstants"
```

### Task 1.2: BallState & AppPhase enums

**Files:**
- Create: `windows/AccountaBall.Core/Models/BallState.cs`, `Models/AppPhase.cs`
- Test: `windows/AccountaBall.Core.Tests/PhaseEnumTests.cs`

**Step 1: Failing test** — assert the enum members exist (incl. FreeBall phases
and the `Observing` ball state from the FreeBall design):
```csharp
[Fact] public void Phases_CoverAllMacOSCases() {
    _ = AppPhase.Idle; _ = AppPhase.Welcome; _ = AppPhase.Setup; _ = AppPhase.Session;
    _ = AppPhase.WhatsUp; _ = AppPhase.Ambiguous; _ = AppPhase.OffTask; _ = AppPhase.Progress;
    _ = AppPhase.Complete; _ = AppPhase.AiUnavailable;
    _ = AppPhase.FreeBall; _ = AppPhase.FreeBallLog; _ = AppPhase.FreeBallRecap; _ = AppPhase.FreeBallHistory;
    _ = BallState.Idle; _ = BallState.OnTask; _ = BallState.OffTask; _ = BallState.Done; _ = BallState.Observing;
}
```
**Step 2:** Run filtered test → FAIL.
**Step 3: Implement** — port `Models/AppPhase.swift` and `Models/BallState.swift`
(add `Observing` to BallState per `2026-06-09-freeball-design.md` §1):
```csharp
namespace AccountaBall.Core.Models;
public enum AppPhase { Idle, Welcome, Setup, Session, WhatsUp, Ambiguous, OffTask,
    Progress, Complete, AiUnavailable, FreeBall, FreeBallLog, FreeBallRecap, FreeBallHistory }
public enum BallState { Idle, OnTask, OffTask, Done, Observing }
```
**Step 4:** Run → PASS. **Step 5: Commit** `feat(win): port AppPhase + BallState enums`.

### Task 1.3: MultiTaskResult + parse (the model output contract)

**Files:**
- Create: `windows/AccountaBall.Core/Models/MultiTaskResult.cs`
- Test: `windows/AccountaBall.Core.Tests/MultiTaskResultParseTests.cs`

**Step 1: Failing tests** — port every case in
`src/Tests/AccountaBallTests/` that exercises `MultiTaskResult.parse`
(read the Swift tests first), plus the core matrix:
```csharp
[Theory]
[InlineData("TASK:0 | Editing the deck", "OnTask", 0, "Editing the deck")]
[InlineData("AMBIGUOUS | Reading docs", "Ambiguous", -1, "Reading docs")]
[InlineData("OFFTASK | YouTube", "OffTask", -1, "YouTube")]
[InlineData("DONE:2 | proposal sent", "Done", 2, "proposal sent")]
[InlineData("offtask", "OffTask", -1, "")]            // case-insensitive keyword
[InlineData("garbage text", "OffTask", -1, "")]       // unknown → safe default offTask
public void Parse_MapsKeywordsAndLabels(string raw, string kind, int idx, string label) { /* assert */ }
```
**Step 2:** Run → FAIL.
**Step 3: Implement** — faithful port of `Models/MultiTaskResult.swift`. Use a
discriminated-union-style record hierarchy or a struct with a `Kind` enum +
`Index`/`Label`. Preserve: split on first `|` only, keyword trimmed +
uppercased, label trimmed with original case, `TASK:`/`DONE:` integer suffix,
**unknown keyword falls back to OffTask** (the Swift default).
**Step 4:** Run → PASS. **Step 5: Commit** `feat(win): port MultiTaskResult + parse`.

### Task 1.4: AiPrompts (verbatim)

**Files:**
- Create: `windows/AccountaBall.Core/Util/AiPrompts.cs`
- Test: `windows/AccountaBall.Core.Tests/AiPromptsTests.cs`

**Step 1:** Read `src/Sources/AccountaBall/Util/AIPrompts.swift` in full. The
prompt strings must be copied **character-for-character** — the `RESULT | <label>`
contract and the bias-toward-ON/AMBIGUOUS instruction are load-bearing.
**Step 2: Failing test** — assert the classify prompt contains the exact anchor
substrings (the `RESULT |` token, the `TASK:N`/`OFFTASK`/`AMBIGUOUS`/`DONE:N`
vocabulary, and the bias sentence) so a paraphrase regression fails the build.
**Step 3: Implement** — port each prompt builder method/string verbatim.
**Step 4:** Run → PASS. **Step 5: Commit** `feat(win): port AiPrompts verbatim`.

---

## Milestone 2 — Core engine + models (TDD, ported tests)

> Read each named Swift file as the spec before porting; port its companion test
> file in the same task so parity is provable. Keep the per-task rhythm: port the
> Swift test → watch it fail → port the implementation → watch it pass → commit.

### Task 2.1: Task/session value models
Port `Models/TaskItem.swift`, `TaskSession.swift`, `Snapshots.swift`,
`MultiTaskResult` consumers' DTOs. Tests: the corresponding `*Tests.swift`.

### Task 2.2: Pure utilities
Port `Util/TaskMatcher.swift`, `Util/TimelineCoalescer.swift`,
`Util/DurationDelta.swift`, `Util/ScreenText.swift` + their tests. These are
pure functions — direct, high-value, fully unit-tested.

### Task 2.3: AppState + drift engine derivations
Port `Models/AppState.swift` focusing on the **derived** values: `driftCount`
(count of `kind=="offtask"` justification events this session),
`commitmentBroken` (`driftCount >= driftLimit`), `askedActivities` set. Port the
`AppStateV2Tests` / drift tests. **Do not** introduce a stored drift tally.

### Task 2.4: AccountabilityEngine — decision logic
Port `Engine/AccountabilityEngine.swift` against fake `IAiService`/`IStore`.
Cover, with ported tests: ON→silent+credit+reset counter; DONE→complete;
AMBIGUOUS→ask once (dedupe via `askedActivities`), not a drift, resets counter;
OFF→2-consecutive rule outside the 15s settle window→confirmed drift; timed break
suppresses prompting/drift; cancelled vs failed classify (skip cycle vs
`aiUnavailable`); auto-return logging. This is the heart — translate behavior
exactly; the `~299` macOS tests are the parity bar.

### Task 2.5: SessionRecap / transparency log
Port `Models/SessionRecap.swift` + `ExcuseVerdict.swift` + `TaskRecap.swift` and
the recap builder. Tests: recap built from persisted records (faithful even if AI
commentary fails), the `◐/●/○` check rendering, the drift-summary / broken-commitment line.

### Task 2.6: FreeBall engine + models
Port `Engine/FreeBallEngine.swift`, `Models/FreeBallModels.swift`,
`Models/FreeBallRecap.swift`, `Util/FreeBallDedup.swift` (Jaccard ≥ 0.85),
`Util/FreeBallCondenser.swift`, `Util/FreeBallMarkdown.swift`,
`Util/FreeBallExport.swift` + their tests. Mid-session: zero AI, store full OCR,
dedupe by extending the prior block.

**Gate:** at the end of Milestone 2, `dotnet test windows/AccountaBall.sln`
passes with the full ported suite. **This is the parity proof.** Do not start the
shell until this is green.

---

## Milestone 3 — Platform integrations (Windows-only)

> Add `.Platform` + `.App` to the solution now (they need the Windows App SDK
> workload and a Windows 11 machine). These are integration-tested + manual, not
> pure-unit — keep interfaces in `.Core`, implementations in `.Platform`.

### Task 3.1: Interfaces in Core
Create `Core/Services/IScreenCapture.cs`, `IOcrService.cs`, `INotifier.cs`,
`Core/Storage/IStore.cs`. The engine already depends on these from Milestone 2's
fakes — now they get real implementations.

### Task 3.2: IOcrService → WindowsMediaOcrService
`Windows.Media.Ocr.OcrEngine.TryCreateFromUserProfileLanguages()`; SoftwareBitmap
→ `RecognizeAsync` → text; off the UI thread. Manual test: known image → expected
text. (OCR is the easiest native piece — do it first to de-risk the pipeline.)

### Task 3.3: IScreenCapture → GraphicsCaptureService
`Windows.Graphics.Capture`: focused window via `GetForegroundWindow` +
`GraphicsCaptureItem.CreateForWindow`; periodic monitor pass via
`CreateForMonitor`; `Direct3D11CaptureFramePool` single-frame every 3s. Feed
frames into Task 3.2. Manual test: capture reads the focused window's text.

### Task 3.4: IStore → SqliteStore (EF Core)
DbContext + entities: `WorkSession`, `TimelineEntry`, `JustificationEvent`,
`KnowledgeTask`, `Allowance`, `TaskCompletion`, `FreeBallSession`,
`FreeBallCapture`. DB at `%LOCALAPPDATA%\AccountaBall\accountaball.db`. EF
migration. Integration test against a temp SQLite file: write events → query
derived `driftCount`. Light settings (declared tasks, drift limit) →
`ApplicationData.LocalSettings` or `config.json`.

### Task 3.5: INotifier → ToastNotifier
`AppNotificationManager` toasts; register AppUserModelID. Manual test: a nudge
fires a visible toast.

### Task 3.6: OllamaAiService
`HttpClient` POST to `OLLAMA_HOST` `/api/generate`, model from `OLLAMA_MODEL`
(default `qwen2.5:7b`), temperature 0, prompts from `AiPrompts`. Config: env var
first, then `%LOCALAPPDATA%\AccountaBall\config.json`. Add the **live Ollama
integration test** (skippable when no daemon): known screen text →
bias-toward-ON/AMBIGUOUS holds (port the macOS 3-state-bias test).

---

## Milestone 4 — WinUI 3 shell

### Task 4.1: Borderless no-activate floating window
`FloatingPanel.cs`: `OverlappedPresenter` borderless, `IsAlwaysOnTop`, off the
switcher; `WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW` via `SetWindowLong` interop so the
ball never steals focus. Per-`AppPhase` `AppWindow` resize (port
`FloatingPanel.resize(for:)`). Manual test: clicking the ball does not defocus the
foreground app.

### Task 4.2: The ball + composition
Round transparent basketball via `Microsoft.UI.Composition`; per-`BallState`
faces incl. the calm `.Observing` (FreeBall). Port `Views/BallView`,
`BasketballView`, `SessionBallView`.

### Task 4.3: Phase views
Port the cards one task each: `WelcomeView`, `TaskSetupView` (incl. the drift-limit
stepper, default 3, range 1–10, setup-only), `AmbiguousAskView`, `OffTaskView`,
`CompletionView` (recap + transparency log), `ProgressView`,
`AiUnavailableView` (the guided "needs Ollama — [Download] [Retry]" card from
design §6.2), `FreeBallView`, `FreeBallHistoryView`. A `RootCoordinator` selects
the view by `AppPhase`.

### Task 4.4: Wire the shell to the engine
`App.xaml.cs` startup: build the store, providers, engine; start the capture loop;
bind phase → window resize + view; route engine events to toasts. Port
`AppDelegate.swift` wiring.

### Task 4.5: Manual "feel it" QA pass
Mirror the macOS manual QA checklist: capture reads focused window, no-activate
holds, cards size right, toasts fire, recap renders from persisted records, a full
on→ambiguous→off→break→complete loop behaves like the Mac, and a FreeBall
session records + recaps.

---

## Milestone 5 — Distribution (deferred, not blocking)
MSIX packaging + code signing (cert ~$100–400/yr) for release; portable zip for
early testers (SmartScreen warns). First-run Ollama guidance verified end-to-end.

---

## Deferred to Phase 2 (parity-neutral, matches macOS roadmap)
OpenRouter provider, cross-session streak, optional commitment line, adaptive
cadence, anti-gaming clustering, a settings UI.
