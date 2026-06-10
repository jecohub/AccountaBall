# AccountaBall — Windows Port (design)

**Date:** 2026-06-10
**Status:** Design APPROVED (Sections 1–6)
**Goal:** Ship a native Windows app at **full feature parity** with the current
macOS app (Phase-1 3-state accountability spine + FreeBall), for **distribution
to others**.

---

## Strategy (locked decisions)

1. **Approach A — separate native Windows app.** The Swift/macOS app stays
   untouched and serves as the reference implementation. The Windows app is a
   second native codebase. The two share **specs and prompts**, never code.
   Rationale: almost none of the macOS code is portable (SwiftUI/NSPanel,
   ScreenCaptureKit, Vision, SwiftData are all macOS-only), and the hard parts
   (capture, OCR, overlay window) are irreducibly platform-specific — a
   "cross-platform" stack (Tauri/Electron/Flutter) would save little while
   forcing us to discard a finished, polished macOS app.
2. **Stack — C#/.NET 8 + WinUI 3 (Windows App SDK).** First-party Windows
   equivalents exist for every macOS dependency; no third-party capture/OCR
   libraries needed.
3. **Scope — full parity** = the **Phase-1 spine** (3-state classify, AMBIGUOUS
   ask, OFF break/resume, deterministic drift engine vs. a setup-locked limit,
   transparency-log recap) **+ FreeBall** (passive observation mode). This is
   exactly what is built and stable on macOS today. Phase-2/3 macOS features are
   deferred here too, so this is parity, not regression.
4. **AI provider — Ollama only for v1.** Ship just `OllamaAiService`. Keep the
   `IAiService` interface so OpenRouter can drop in later, but no provider-switch,
   no `AI_PROVIDER` branch, no OpenRouter key handling in v1. OpenRouter is a
   Phase-2 add.
5. **Target — Windows 11 minimum** (mirrors macOS being Sonoma 14+).

---

## Section 1 — Layer-by-layer mapping

The macOS app is already cleanly split into a **portable brain** (deterministic
engine + prompts + state machine — "Architecture B: model perceives, Swift
decides") and a **macOS-native shell** (UI, capture, OCR, storage). The port
re-implements only the shell; the brain is a faithful C# translation of logic
that is already platform-agnostic by design. That split is what makes this
tractable.

| Concern | macOS (today) | Windows (port) |
|---|---|---|
| Floating ball window | SwiftUI + NSPanel | WinUI 3 borderless always-on-top window (`AppWindow`, `OverlappedPresenter`, no-activate) |
| Screen capture | ScreenCaptureKit | `Windows.Graphics.Capture` |
| OCR (offline) | Vision.framework | `Windows.Media.Ocr` (offline, built-in) |
| Durable history | SwiftData | SQLite via EF Core |
| Light settings | UserDefaults | `ApplicationData.LocalSettings` / JSON |
| Notifications | UNUserNotifications | `AppNotificationManager` (Windows toasts) |
| AI provider | URLSession → Ollama | `HttpClient` → Ollama (same endpoint/prompt) |
| Engine / state machine | Swift | C# (direct behavioral translation) |

Every macOS dependency has a first-party Windows equivalent.

---

## Section 2 — Project structure & the portable brain

Solution layout mirrors the Swift `Sources/AccountaBall/` tree so the two stay
legible side by side:

```
AccountaBall.Windows/
  AccountaBall.Core/          ← the portable brain (no UI, no OS deps)
    Engine/   AccountabilityEngine.cs, FreeBallEngine.cs
    Models/   AppPhase.cs, AppState.cs, BallState.cs,
              MultiTaskResult.cs, SessionRecap.cs, FreeBall*.cs …
    Services/ IAiService.cs, OllamaAiService.cs
    Util/     AiPrompts.cs, AppConstants.cs, TaskMatcher.cs,
              TimelineCoalescer.cs, FreeBallDedup.cs …
  AccountaBall.Platform/      ← OS-native implementations behind interfaces
    IScreenCapture.cs → GraphicsCaptureService.cs
    IOcrService.cs    → WindowsMediaOcrService.cs
    INotifier.cs      → ToastNotifier.cs
    IStore.cs         → SqliteStore.cs (EF Core)
  AccountaBall.App/            ← WinUI 3 shell (the Views/)
    Views/    BallView, TaskSetupView, AmbiguousAskView,
              OffTaskView, CompletionView, FreeBallView …
    App.xaml, MainWindow, FloatingPanel.cs
  AccountaBall.Core.Tests/    ← xUnit, ports the ~299 Swift tests
```

- `AccountaBall.Core` has **zero** `Windows.*` references — the same logic the
  Swift engine holds, in C#, and the home of the ported unit tests. The engine
  talks to the OS only through `.Platform` interfaces (`IScreenCapture`,
  `IOcrService`, `INotifier`, `IStore`), exactly as `AccountabilityEngine.swift`
  depends on the `AIService` protocol, not a concrete provider.
- **Translation discipline:** a *behavioral* port, not a reinterpretation — same
  cadence (`cycleSeconds = 3`), same 15s settle window, same 2-consecutive-OFF
  rule, same derived `driftCount`, same temperature-0 prompts copied **verbatim**
  from `AIPrompts.swift`. The ported tests prove parity.

---

## Section 3 — The native shell (capture, OCR, the ball)

The real Windows-specific work. Three pieces, each with a known gotcha:

**1. The floating ball window.** WinUI 3 has no NSPanel; compose it:
- Borderless, always-on-top, off the taskbar: `OverlappedPresenter` with
  `SetBorderAndTitleBar(false, false)`, `IsAlwaysOnTop = true`,
  `AppWindow.IsShownInSwitchers = false`.
- **No-activate (the gotcha):** clicking the ball must not steal focus from the
  user's work. Set `WS_EX_NOACTIVATE` + `WS_EX_TOOLWINDOW` extended styles via
  `SetWindowLong` interop on the HWND — WinUI doesn't expose this directly.
- Per-phase resize (`FloatingPanel.resize(for:)` today) → resize `AppWindow` per
  `AppPhase`; ball is small, cards grow the window.
- Round, transparent, draggable basketball via `Microsoft.UI.Composition` with a
  transparent backdrop.

**2. Screen capture — `Windows.Graphics.Capture`.**
- Prefer the **focused window** (mirrors today's focused-window + truncated
  full-screen pass): `GetForegroundWindow` → `GraphicsCaptureItem.CreateForWindow`.
  Periodic full-screen pass uses `CreateForMonitor`.
- `Direct3D11CaptureFramePool` pulled single-frame every 3s (not a live stream) —
  lighter, matches cadence.
- Gotcha: Win10 pre-20H1 shows a yellow capture border; Win11 is clean → **Win11
  target** avoids it.

**3. OCR — `Windows.Media.Ocr`.**
`OcrEngine.TryCreateFromUserProfileLanguages()` — offline, ships with Windows, no
model download. Frame → `SoftwareBitmap` → `RecognizeAsync` → text. Direct analog
of the Vision pass; runs off the UI thread.

**Permissions:** app-initiated Graphics Capture needs no special grant on Win11
(a friction *reduction* vs. macOS Screen Recording). Toasts require an
AppUserModelID registration.

---

## Section 4 — AI provider, storage & config

**AI provider (Ollama only, v1).** `OllamaAiService : IAiService`, sharing one
`AiPrompts.cs`, decoding at **temperature 0**: `HttpClient` POST to `OLLAMA_HOST`
(`http://localhost:11434`) `/api/generate`, model `qwen2.5:7b`. Identical request
shape and the same one-line `RESULT | <label>` contract parsed by
`MultiTaskResult.Parse` (incl. the `AMBIGUOUS` token) — which is exactly why the
prompt strings are copied verbatim, not paraphrased. `IAiService` is kept so
OpenRouter can drop in for Phase 2.

**Config.** `OLLAMA_HOST` / `OLLAMA_MODEL` read via
`Environment.GetEnvironmentVariable`, **plus** a fallback
`%LOCALAPPDATA%\AccountaBall\config.json`. The fallback exists because a
double-clicked packaged Windows GUI app doesn't inherit a shell's env vars the way
`make run` does on macOS. Env var wins if set; JSON is the durable store
(Phase-2: a settings UI).

**Storage (three layers, same as macOS):**
- **SQLite via EF Core** replaces SwiftData: `WorkSession`, `TimelineEntry`,
  `JustificationEvent`, `KnowledgeTask`, `Allowance`, `TaskCompletion`, plus
  FreeBall's `FreeBallSession` / `FreeBallCapture`. DB at
  `%LOCALAPPDATA%\AccountaBall\accountaball.db`. `driftCount` stays **derived** (a
  query: count of `kind == "offtask"` events) — never a stored tally, exactly as
  today.
- **LocalSettings / JSON** replaces UserDefaults — declared tasks + drift limit.
- **Rotating debug log** at `%LOCALAPPDATA%\AccountaBall\logs\accountaball.log`.

---

## Section 5 — Testing & parity verification

Parity is not a claim — it is the ported test suite passing.

1. **Ported unit tests (xUnit).** The ~299 Swift engine tests translate directly
   because the brain is pure logic: derived `driftCount`, threshold →
   `commitmentBroken`, ask-once dedup (`askedActivities`), the 2-consecutive-OFF
   rule, the 15s settle window, `MultiTaskResult.Parse` (incl. `AMBIGUOUS`),
   `TimelineCoalescer`, `FreeBallDedup`. Run with **fake**
   `IScreenCapture`/`IOcrService`/`IAiService`/`IStore` — no OS, no model. This
   suite *is* the definition of behavioral parity.
2. **Live Ollama integration test.** Port the macOS 3-state-bias test: feed known
   screen text to a real local `qwen2.5:7b` and assert the
   bias-toward-ON/AMBIGUOUS holds. Same model + verbatim prompt → should pass
   identically; it proves the prompt port worked.
3. **Manual "feel it" pass.** Mirror the macOS manual QA checklist — capture reads
   the focused window, the ball stays out of focus (no-activate works), cards size
   correctly, toasts fire, the recap renders from persisted records.

---

## Section 6 — Open decisions, risks & distribution

1. **Windows 11 only (v1)** — clean borderless Graphics Capture + modern toast
   stack; avoids the Win10 capture border. Mirrors macOS Sonoma-14+.
2. **The Ollama first-run cliff (the real risk).** Each distributed user needs
   Ollama + a ~4.7 GB `qwen2.5:7b` pull before the app works. Silent failure would
   kill adoption. **Mitigation:** port the existing `aiUnavailable` phase + health
   polling, but show a *guided* card — "AccountaBall needs Ollama — [Download]
   [I've installed it, retry]" — so the app is honest and unblocked, never
   mysteriously dead.
3. **Distribution & code signing.** Unsigned apps trigger SmartScreen ("Unknown
   publisher"). **Recommendation:** MSIX installer (clean install/update; needs a
   signing cert, ~$100–400/yr) for release; a portable zip (SmartScreen warns) for
   early testers. Flagged, not blocking.
4. **Deferred to Phase 2** (matching the macOS roadmap, not regressions):
   OpenRouter provider, cross-session streak, optional commitment line, adaptive
   cadence, anti-gaming clustering.
5. **Effort reality.** A substantial port — new WinUI shell, three native
   integrations (capture/OCR/toast), an EF Core store, ~300 translated tests.
   Tractable because the brain is pure logic, but not a weekend.

---

## Code map cross-reference (macOS → port)

| Concern | macOS file | Windows file |
|---|---|---|
| Monitoring loop + decisions + drift engine | `Engine/AccountabilityEngine.swift` | `Core/Engine/AccountabilityEngine.cs` |
| Passive observation mode | `Engine/FreeBallEngine.swift` | `Core/Engine/FreeBallEngine.cs` |
| App state / phases / drift limit | `Models/{AppState,AppPhase}.swift` | `Core/Models/{AppState,AppPhase}.cs` |
| 3-state result + parse | `Models/MultiTaskResult.swift` | `Core/Models/MultiTaskResult.cs` |
| Persistence entities + check log | `Models/Persistence.swift` | `Platform/SqliteStore.cs` + `Core/Models` |
| Recap / transparency log | `Models/SessionRecap.swift` | `Core/Models/SessionRecap.cs` |
| Shared AI prompts (verbatim) | `Util/AIPrompts.swift` | `Core/Util/AiPrompts.cs` |
| Ollama provider | `Services/OllamaAIService.swift` | `Core/Services/OllamaAiService.cs` |
| Capture / OCR / notifications | `Services/{ScreenCapture,OCR,Notification}Service.swift` | `Platform/{GraphicsCaptureService,WindowsMediaOcrService,ToastNotifier}.cs` |
| The cards / ball | `Views/*.swift` | `App/Views/*` |

**Next step:** when ready to implement, create the implementation plan
(`superpowers:writing-plans`) — task-by-task TDD, starting with the
`AccountaBall.Core` brain + ported tests (provable parity before any pixel of
WinUI), then the `.Platform` integrations, then the WinUI shell.
