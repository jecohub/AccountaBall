# 2026-06-03 — Default AI provider = local Ollama; OpenRouter optional

## Status
Accepted

## Context
v2 called a cloud LLM for every ~5s capture cycle (classification) plus excuse
evaluation, task summaries, and matching. That is a continuous cost and a privacy
concern: screen OCR text is sent to a third party on every cycle. v2 also shipped
a hardcoded OpenRouter API key in `AppDelegate.swift` (now removed from source,
but still present in git history — **rotate it**).

The classification/justification/recap prompts are small and well within the
reach of a local 7B-class model, so a local default is viable.

## Decision
Default the AI provider to **local Ollama**, selectable via the `AI_PROVIDER`
env var (`ollama` default, `openrouter` optional). The provider is constructed by
an env-driven factory in `AppDelegate`:

- `ollama`: `OLLAMA_HOST` (default `http://localhost:11434`), `OLLAMA_MODEL`
  (default `qwen2.5:7b`).
- `openrouter`: `OPENROUTER_API_KEY` (required, never hardcoded),
  `OPENROUTER_MODEL` (default `anthropic/claude-haiku-4-5`).

No automatic fallback between providers: if the requested provider is
misconfigured (e.g. `openrouter` without a key), the app sets `state.setupHint`
and still launches on Ollama so the user sees the hint rather than a silent
cloud call or a crash. Claude remains a legacy/secondary path (stubs only for v3
features).

## Consequences
- Default runs are free and fully local — screen text never leaves the machine.
- Requires Ollama installed and running with a pulled model for the default path;
  surfaced to the user via `setupHint` when unavailable.
- Cloud quality (OpenRouter) is one env var away for users who want it.
- The leaked v2 key must be rotated at openrouter.ai; it survives in git history.

## Alternatives considered
- **Cloud-only (status quo)**: ongoing cost + privacy exposure on every cycle.
- **Auto-fallback cloud→local**: hides misconfiguration and could leak data to
  the cloud unexpectedly; explicit selection + hint is clearer.
