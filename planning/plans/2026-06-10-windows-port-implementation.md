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

## ✅ BUILD PROGRESS (last updated 2026-06-10)

The .NET solution lives in `windows/` (classic `.sln`, projects target `net8.0`).
**Done so far — 92 unit tests green, all committed:**

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

- [x] **M2.6** — FreeBall passive mode: `FreeBallSession`/`FreeBallCapture`/
      `CategorySpan`, `FreeBallRecap`/`Summary`/`TranscriptEntry`/`PastRecap`;
      `FreeBallDedup`/`Condenser`/`Markdown`/`Export`; `AiPrompts.BuildFreeBallPrompt`
      + `ParseFreeBallSummary`; `IAiService.SummarizeFreeBallAsync`; `IStore` FreeBall
      ops; `AppState.FreeBallRecap`; `FreeBallEngine` (`Begin`/`Ingest`/`EndAsync`).
      16 tests. **The dwell OCR-wobble fix** also landed in both Core + Swift.

**FreeBall Core API for the App layer (M4):** `new FreeBallEngine(state, ai) { Store = ... }`;
call `Begin()` on "Just observe", drive `Ingest(ocrText)` from the Platform capture
loop each cycle, `await EndAsync()` on "End Session". Recap is published to
`state.FreeBallRecap`; history via `store.FreeBallSessions` + `FreeBallRecap.FromSession(s)`;
export via `FreeBallMarkdown.Render` / `FreeBallExport`.

**Next up:**
- [ ] **M2.5** — completion + recap + match: `SummarizeCompletionAsync`
      (KnowledgeTask/TaskCompletion), `FinalizeSessionRecapAsync` (the recap +
      transparency log), `ProposeMatchAsync` (cheap + AI match). Port
      `EngineCompletionTests`, `EngineMatchTests`, `EngineSessionRecapTests`. Wire
      the fire-and-forget summarizeCompletion into `CompleteTask`, and the
      allowance-confirm-on-reuse queue (`linkKnowledgeTask`/confirm/reject).
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
