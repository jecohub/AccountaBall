using AccountaBall.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace AccountaBall.Platform.Storage;

/// EF Core mapping for the Core history POCOs (the Windows backing for SwiftData).
/// The POCOs live in Core and carry no EF annotations or key properties — by
/// design, Core stays persistence-agnostic — so every key, relationship, and
/// collection is configured here via the Fluent API:
///   * shadow integer primary keys for keyless entities; real Guid keys for
///     entities that carry an `Id` (KnowledgeTask, FreeBallSession, WorkSession,
///     and the M1 spine entities Capture / Project / Thread / Contribution /
///     Judgment);
///   * EF Core 8 primitive-collection mapping for the `List&lt;string&gt;`
///     properties (TaskTitles / OriginalTitles / Steps; Project's Aliases /
///     People / CodeContext / Refs), stored as JSON;
///   * owned cascade children via the get-only navigation collections.
public sealed class AccountaBallDbContext : DbContext
{
    public DbSet<WorkSession> Sessions => Set<WorkSession>();
    public DbSet<KnowledgeTask> KnowledgeTasks => Set<KnowledgeTask>();
    public DbSet<FreeBallSession> FreeBallSessions => Set<FreeBallSession>();
    // Context-spine M1 (parity with macOS src master 332ce78).
    public DbSet<Capture> Captures => Set<Capture>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<Contribution> Contributions => Set<Contribution>();
    public DbSet<Judgment> Judgments => Set<Judgment>();

    public AccountaBallDbContext(DbContextOptions<AccountaBallDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<WorkSession>(e =>
        {
            e.HasKey(s => s.Id);                       // real Guid key (W4 — captures reference it by SessionId)
            e.Property(s => s.StartedAt);
            e.Property(s => s.EndedAt);
            e.PrimitiveCollection(s => s.TaskTitles);
            e.HasMany(s => s.Entries).WithOne().OnDelete(DeleteBehavior.Cascade);
            e.HasMany(s => s.Justifications).WithOne().OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<TimelineEntry>(e =>
        {
            ShadowKey(e);
            e.Property(t => t.At);
            e.Property(t => t.TaskIndex);
            e.Property(t => t.Label);
        });

        b.Entity<JustificationEvent>(e =>
        {
            ShadowKey(e);
            e.Property(j => j.At);
            e.Property(j => j.Excuse);
            e.Property(j => j.Justified);
            e.Property(j => j.InferredTaskIndex);
            e.Property(j => j.Activity);
            e.Property(j => j.Rule);
            e.Property(j => j.Kind);
        });

        b.Entity<KnowledgeTask>(e =>
        {
            e.HasKey(k => k.Id);                       // real Guid key on this entity
            e.Property(k => k.NormalizedTitle);
            e.Property(k => k.LastCompletedAt);
            e.Property(k => k.TimesCompleted);
            e.PrimitiveCollection(k => k.OriginalTitles);
            e.HasMany(k => k.Allowances).WithOne().OnDelete(DeleteBehavior.Cascade);
            e.HasMany(k => k.Completions).WithOne().OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(k => k.NormalizedTitle);        // engine looks up by normalized title
        });

        b.Entity<Allowance>(e =>
        {
            ShadowKey(e);
            e.Property(a => a.Rule);
            e.Property(a => a.CreatedAt);
            e.Property(a => a.NeedsConfirmation);
        });

        b.Entity<TaskCompletion>(e =>
        {
            ShadowKey(e);
            e.Property(c => c.CompletedAt);
            e.Property(c => c.Duration);
            e.Property(c => c.Summary);
            e.Property(c => c.OffTaskCount);
            e.PrimitiveCollection(c => c.Steps);
        });

        b.Entity<FreeBallSession>(e =>
        {
            e.HasKey(s => s.Id);                       // real Guid key (like KnowledgeTask)
            e.Property(s => s.StartedAt);
            e.Property(s => s.EndedAt);
            e.Property(s => s.CycleCount);
            e.Property(s => s.Narrative);
            e.Property(s => s.Insight);
            e.Property(s => s.RecapPending);
            // Distilled context lists → JSON columns (EF8 primitive collections).
            e.PrimitiveCollection(s => s.WorkingOn);
            e.PrimitiveCollection(s => s.People);
            e.PrimitiveCollection(s => s.CodeContext);
            e.PrimitiveCollection(s => s.OpenThreads);
            // The categorized breakdown is a small value-object list → JSON column.
            e.OwnsMany(s => s.Categories, o => o.ToJson());
            // The raw deduped transcript is the bulk child table.
            e.HasMany(s => s.Captures).WithOne().OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<FreeBallCapture>(e =>
        {
            ShadowKey(e);
            e.Property(c => c.FirstSeenAt);
            e.Property(c => c.LastSeenAt);
            e.Property(c => c.Text);
            e.Ignore(c => c.Seconds);                  // computed (no setter); not persisted
        });

        // ── Context spine (M1) ──────────────────────────────────────────────
        // Shared capture written by task mode (FreeBall still uses FreeBallCapture
        // until convergence). Soft-referenced to its session by SessionId (no EF
        // relationship) so it can later span WorkSession/FreeBallSession.
        b.Entity<Capture>(e =>
        {
            e.HasKey(c => c.Id);                       // real Guid key
            e.Property(c => c.FirstSeenAt);
            e.Property(c => c.LastSeenAt);
            e.Property(c => c.Text);
            e.Property(c => c.Mode);
            e.Property(c => c.AppHint);
            e.Property(c => c.TaskIndex);
            e.Property(c => c.SessionId);
            e.Ignore(c => c.Seconds);                  // computed (no setter); not persisted
            // IngestCapture queries WHERE SessionId = ? ORDER BY LastSeenAt DESC LIMIT 1.
            e.HasIndex(c => new { c.SessionId, c.LastSeenAt });
        });

        b.Entity<Project>(e =>
        {
            e.HasKey(p => p.Id);                       // real Guid key
            e.Property(p => p.Title);
            e.Property(p => p.Status);
            e.Property(p => p.CreatedAt);
            e.Property(p => p.LastTouchedAt);
            e.Property(p => p.Summary);
            e.PrimitiveCollection(p => p.Aliases);     // List<string> → JSON
            e.PrimitiveCollection(p => p.People);
            e.PrimitiveCollection(p => p.CodeContext);
            e.PrimitiveCollection(p => p.Refs);
            e.HasMany(p => p.Threads).WithOne().OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Thread>(e =>
        {
            e.HasKey(t => t.Id);                       // real Guid key
            e.Property(t => t.Title);
            e.Property(t => t.Status);
            e.Property(t => t.OpenedAt);
            e.Property(t => t.ResolvedAt);
        });

        // Contribution/Judgment link by Guid soft-ref (no navigation properties).
        b.Entity<Contribution>(e =>
        {
            e.HasKey(c => c.Id);                       // real Guid key
            e.Property(c => c.At);
            e.Property(c => c.ProjectId);
            e.Property(c => c.ThreadId);
            e.Property(c => c.SessionId);
            e.Property(c => c.SessionKind);
            e.Property(c => c.Minutes);
            e.Property(c => c.Summary);
        });

        b.Entity<Judgment>(e =>
        {
            e.HasKey(j => j.Id);                       // real Guid key
            e.Property(j => j.At);
            e.Property(j => j.CaptureRangeStart);
            e.Property(j => j.CaptureRangeEnd);
            e.Property(j => j.ModelProposal);
            e.Property(j => j.UserDecision);
        });
    }

    /// Add an auto-increment shadow PK for a POCO that has no key property.
    private static void ShadowKey<T>(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<T> e)
        where T : class
    {
        e.Property<int>("Id").ValueGeneratedOnAdd();
        e.HasKey("Id");
    }
}
