# FreeBall Context-Aware Recap — Design

**Date:** 2026-06-10
**Status:** Approved (all sections)
**Builds on:** `2026-06-09-freeball-design.md` / `2026-06-09-freeball-implementation.md` (FreeBall v1, shipped)

## Problem

FreeBall v1's End-Session recap gives a *time report*: a narrative ("you mostly
coded"), categorized minutes ("Coding 45m, Email 10m"), and one habit insight.
In real use this is too thin — the user wants the **substance** of the session:
what they were working on, who they were talking to, what they were coding, and
what's left open. And once shown, that context was buried: displayed for ~10s in
the recap, then persisted only inside the SwiftData store with no way to retrieve
it. If the point of passive observation is to *use* the context later (resume
work, hand to an AI), it needs an exit door.

## Goals

1. **Keep** the existing recap (narrative + time breakdown + insight) and **add**
   extracted context on top — not replace it.
2. Extract four context dimensions, grounded in the actual screen transcript:
   **Working on**, **People & conversations**, **Code context**,
   **Open threads / next steps**.
3. Make stored context **retrievable**: an in-app **history browser** and
   **Markdown export**.

## Non-goals (YAGNI)

- No richer structured entities (a `Person` struct etc.) — plain phrase strings.
- No search/filter, bulk export, or in-UI delete in the browser.
- No clear-history affordance yet (store still "kept forever"; fast-follow).

---

## Section 1 — Data shape

`FreeBallSummary`, `FreeBallRecap`, and the `FreeBallSession` `@Model` each gain
four context lists alongside the existing fields:

```swift
struct FreeBallSummary {
    let narrative: String           // existing
    let categories: [CategorySpan]  // existing
    let insight: String             // existing
    let workingOn: [String]         // NEW — active tasks/projects
    let people: [String]            // NEW — who + gist ("Sarah (Slack) — launch Fri")
    let codeContext: [String]       // NEW — files/functions/errors touched
    let openThreads: [String]       // NEW — unfinished items / next steps
}
```

- `FreeBallSession` stores the four arrays as `Codable [String]` (same pattern as
  `categories`).
- `parseFreeBallSummary` parses the four new JSON arrays, each defaulting to `[]`
  on absence — a model that omits a field degrades gracefully, never crashes.
- Empty fields are not rendered (no empty headers).
- **Reason for `[String]` over structured types:** a local 7B model produces
  cleaner output asking for plain phrases than nested objects; the UI just lists
  them. Structure can come later.

---

## Section 2 — Prompt & extraction

`freeBallSystem` is rewritten to return the existing three outputs *plus* the four
context lists in **one JSON object** (still a single AI call at End Session):

```json
{ "narrative": "...", "categories": [...], "insight": "...",
  "workingOn": ["..."], "people": ["..."], "codeContext": ["..."], "openThreads": ["..."] }
```

Prompt guards (local 7B model over a noisy OCR transcript):
- **Ground every item in the transcript** — "only list a person/file/thread that
  actually appears on screen; do not invent." Main hallucination guard.
- **Empty is fine** — "return an empty array if a category has nothing." Blank
  beats filler.
- **Short concrete phrases** — `Sarah (Slack) — wants launch Friday`, not prose.
- `openThreads` framed as "unfinished / awaiting-reply / undecided" — what makes
  resuming possible.

Supporting changes for quality:
1. **Bigger transcript budget** — raise `freeBallMaxTranscriptChars` to ≈24–32k
   (timeout already fixed) so names/filenames/threads survive truncation. Tuning
   knob, not structural.
2. **Past-session context feed-forward** — the prior-sessions block (already feeds
   `insight`) also carries the last session's `openThreads`, so "resume" chains
   across sessions.

Trade-off: a 7B model will sometimes miss/garble a name. Grounding reduces
invention; accuracy improves by switching `OLLAMA_MODEL` to a 14B (no code change).

---

## Section 3 — History browser, export & navigation

**One new phase — `.freeBallHistory`** (the browser), reached from a "Past
sessions" link on the Welcome screen under the FreeBall button.

**`FreeBallHistoryView`:**
- Fetches all ended `FreeBallSession`s, newest first. Each row: date, duration,
  one-line narrative, an export button, and an orange dot if `recapPending`.
- Tap a row → opens the **same `FreeBallRecapView`**, populated from that stored
  session (no second renderer).
- `FreeBallRecap` gains the four context fields + a `FreeBallRecap(from:
  FreeBallSession)` initializer. The recap view's `onDone` routes back to history
  (historical view) or welcome (live session).

**Export to Markdown:**
- An **Export** button on the recap and each history row writes a timestamped
  `.md` to `~/Documents/AccountaBall/FreeBall/` and reveals it in Finder
  (`NSWorkspace.activateFileViewerSelecting`) — no save-panel friction for an
  accessory app; the folder becomes a grep-able / AI-feedable archive.
- `FreeBallMarkdown.render(session:)` — pure function, unit-testable. Format:
  title (date · duration) → narrative → time breakdown → Working on / People /
  Code / Open threads → insight.

---

## Testing

- `parseFreeBallSummary` — new fields parse; missing fields default to `[]`;
  bad JSON → empty.
- `buildFreeBallPrompt` — includes transcript + past `openThreads`.
- `FreeBallMarkdown.render` — sections present/omitted correctly; pure function.
- `FreeBallRecap(from:)` — round-trips a stored session.
- Engine/persistence suites extended for the four new stored fields.
- UI (history browser, recap sections, export button) — build + manual smoke.

## Rollout

Ollama-first (the active provider). OpenRouter inherits the shared prompt change;
not separately QA'd. Export folder created on first use.
