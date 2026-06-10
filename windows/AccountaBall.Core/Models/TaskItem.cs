namespace AccountaBall.Core.Models;

/// One declared task. Port of Swift `TaskItem` (Codable/Identifiable/Equatable).
/// `Id` is generated on construction and preserved through JSON round-trips.
public sealed record TaskItem
{
    public System.Guid Id { get; init; } = System.Guid.NewGuid();
    public string Task { get; set; } = string.Empty;
    public string Context { get; set; } = string.Empty;
    public bool IsComplete { get; set; }
    /// Seconds credited to this task (Swift `timeOnTask: TimeInterval`).
    public double TimeOnTask { get; set; }
    public System.Guid? KnowledgeRef { get; set; }

    /// Both task and context are non-blank (whitespace-only does not count).
    public bool IsFilledIn =>
        !string.IsNullOrWhiteSpace(Task) && !string.IsNullOrWhiteSpace(Context);
}
