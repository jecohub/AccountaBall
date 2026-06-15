using AccountaBall.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace AccountaBall.Platform.Storage;

/// EF Core mapping for the Core history POCOs (the Windows backing for SwiftData).
/// The POCOs live in Core and carry no EF annotations or key properties — by
/// design, Core stays persistence-agnostic — so every key, relationship, and
/// collection is configured here via the Fluent API:
///   * shadow integer primary keys for the keyless entities;
///   * EF Core 8 primitive-collection mapping for the `List&lt;string&gt;`
///     properties (TaskTitles / OriginalTitles / Steps), stored as JSON;
///   * owned cascade children via the get-only navigation collections.
public sealed class AccountaBallDbContext : DbContext
{
    public DbSet<WorkSession> Sessions => Set<WorkSession>();
    public DbSet<KnowledgeTask> KnowledgeTasks => Set<KnowledgeTask>();
    public DbSet<FreeBallSession> FreeBallSessions => Set<FreeBallSession>();

    public AccountaBallDbContext(DbContextOptions<AccountaBallDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<WorkSession>(e =>
        {
            ShadowKey(e);
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
    }

    /// Add an auto-increment shadow PK for a POCO that has no key property.
    private static void ShadowKey<T>(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<T> e)
        where T : class
    {
        e.Property<int>("Id").ValueGeneratedOnAdd();
        e.HasKey("Id");
    }
}
