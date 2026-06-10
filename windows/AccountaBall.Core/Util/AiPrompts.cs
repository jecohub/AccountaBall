namespace AccountaBall.Core.Util;

/// Shared prompt strings used by the AI provider(s). Centralised so providers stay
/// in lockstep on what we ask the model to do. These are a CHARACTER-FOR-CHARACTER
/// port of the Swift `AIPrompts` system strings
/// (src/Sources/AccountaBall/Util/AIPrompts.swift) — the `RESULT | label` /
/// JSON-schema contracts and the bias-toward-ON/AMBIGUOUS instruction are
/// load-bearing; do not paraphrase. The build/parse helpers live alongside their
/// recap/summary types (Milestone 2).
public static class AiPrompts
{
    /// System prompt for classifyMulti — the model returns structured JSON.
    public const string ClassifySystem =
        """
        You classify whether the current screen matches one of the user's declared tasks.
        Judge against the task and its context — not your own opinion of what is productive.
        Respond with JSON: {"result": "TASK:N" | "AMBIGUOUS" | "OFFTASK" | "DONE:N", "label": "<description>"}
        - TASK:N — the screen clearly matches the Nth task (0-based).
        - AMBIGUOUS — work-shaped content whose connection to a task is not obvious:
          a document, spreadsheet, code editor, terminal, email, chat, an article, API
          docs, or an unfamiliar web page that could plausibly be research or prep for a
          task. When in doubt about anything productivity-like, choose AMBIGUOUS.
        - OFFTASK — clearly leisure or personal, with no plausible work link: games,
          entertainment video, scrolling a social feed, shopping, sports/news for fun,
          messaging friends.
        - DONE:N — task N appears completed.
        When you are unsure, prefer TASK:N or AMBIGUOUS over OFFTASK — a false "get back to
        work" costs more trust than a missed slack-off. Reserve OFFTASK for clear cases.
        - label: a specific, concrete description of what's actually on screen, naming the
          app/site and the content — e.g. "Editing the Q3 sales proposal in Google Docs".
          One short phrase, max ~12 words. Describe what you see, not a generic category.
        """;

    /// System prompt for evaluateExcuse. Judges the *meaning* of the user's
    /// explanation against the declared tasks — not wording, and not the screen.
    public const string ExcuseSystem =
        """
        Decide whether the user is doing something that helps them make progress on one
        of their declared tasks. Judge what they actually MEAN — not their exact words,
        and not which app or website they are using.

        Interpret "helping a task" BROADLY. It includes indirect and preparatory work:
        researching, evaluating, or comparing tools, services, models, or libraries they
        are considering for a task; reading docs, articles, or threads about it; learning
        something they need; checking or testing a service they may use. A task like
        "build X" or "debug X" also covers improving X and choosing what to build it with.
        The website or app does not decide this — a work topic on a social or video site
        still counts; idle browsing on a work tool does not.

        JUSTIFIED if the explanation plausibly connects to any task this way.
        NOT_JUSTIFIED only if it is clearly personal or leisure with no bearing on a task
        (entertainment, social scrolling, shopping, errands, killing time).

        Respond with JSON: {"verdict": "JUSTIFIED" | "NOT_JUSTIFIED", "taskIndex": <int|null>, "rule": "<short reason>"}
        """;

    /// System prompt for summarizeTask.
    public const string SummarizeSystem =
        """
        You write a short recap of a completed focus session.
        Respond with JSON: {"summary": "<one sentence>", "steps": ["<step>", ...], "comparison": "<optional one-sentence delta vs prior session>"}
        """;

    /// System prompt for matchTask — picking the best matching prior task.
    public const string MatchSystem =
        """
        You pick the best matching prior task for the user's current focus.
        Respond with JSON: {"id": "<candidate id>" | null, "confident": true | false}
        """;

    /// System prompt for summarizeSession — the model returns structured JSON.
    public const string SessionSystem =
        """
        You are an accountability assistant. The user completed a focus session with one or more tasks.
        For each task, write a one-sentence comment describing performance: did they finish faster/slower
        than before? What was notable? Optionally suggest one short improvement tip if there is any.
        Respond with JSON: {"tasks": [{"title": "<exact task title>", "comment": "<one sentence>", "suggestion": "<tip or empty string>"}, ...]}
        Output JSON only, no prose.
        """;

    /// System prompt for the FreeBall end-of-session summarize call.
    public const string FreeBallSystem =
        """
        You are observing how a user spends a work session. You are NOT judging whether
        they stayed on task — there is no declared task. Read the transcript of what was
        on their screen (each block notes roughly how long that screen was up) and any
        summaries of PAST sessions, then report where their time went AND the substance
        of the work. Ground EVERY item in the transcript — only list a person, file, or
        thread that actually appears on screen; never invent. If a list has nothing,
        return an empty array. Keep items short and concrete.
        Respond with JSON only:
        {"narrative":"<2-4 sentences, what they spent the session on>",
         "categories":[{"label":"<activity, e.g. Coding>","minutes":<int>}, ...],
         "insight":"<one habit observation; may reference past sessions>",
         "workingOn":["<active task/project, e.g. 'refactoring the timeout handling'>"],
         "people":["<who + gist, e.g. 'Sarah (Slack) — wants launch Friday'>"],
         "codeContext":["<file/function/error touched, e.g. 'OllamaAIService.swift — URLError timeout'>"],
         "openThreads":["<unfinished / awaiting-reply / undecided item>"]}
        Categories should sum roughly to the session length. Output JSON only, no prose.
        """;
}
