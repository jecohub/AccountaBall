using System.Collections.Generic;

namespace AccountaBall.Core.Models;

/// AI recap of one completed task. Port of Swift `TaskRecap`. `Comparison` is null
/// on first completion.
public sealed record TaskRecap(string Summary, IReadOnlyList<string> Steps, double Duration, string? Comparison);
