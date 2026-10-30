using Atlas.Shared.Applications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Atlas.Review.Api.Data;

/// <summary>
/// A case waiting for a member of staff, as the <c>ReviewQueue</c> view shows it. This view is
/// all the Review API can see of an application.
/// </summary>
public sealed class ReviewCase
{
    /// <summary>The application's unique identifier.</summary>
    public Guid ApplicationId { get; set; }

    /// <summary>The applicant's market.</summary>
    public string Market { get; set; } = string.Empty;

    /// <summary>REVIEW for a compliance review, BRANCH for a branch visit.</summary>
    public string Stage { get; set; } = string.Empty;

    /// <summary>Given name.</summary>
    public string FirstName { get; set; } = string.Empty;

    /// <summary>Family name.</summary>
    public string LastName { get; set; } = string.Empty;

    /// <summary>Date of birth.</summary>
    public DateOnly DateOfBirth { get; set; }

    /// <summary>Nationality code.</summary>
    public string Nationality { get; set; } = string.Empty;

    /// <summary>The national identifier, when there is one.</summary>
    public string? NationalId { get; set; }

    /// <summary>The passport number, when it was given instead.</summary>
    public string? PassportNumber { get; set; }

    /// <summary>The identity provider's verdict on the document.</summary>
    public string? DocumentResult { get; set; }

    /// <summary>Whether the selfie matched the document.</summary>
    public bool? FaceMatch { get; set; }

    /// <summary>The identity provider's confidence.</summary>
    public double? Confidence { get; set; }

    /// <summary>
    /// The screening provider's case reference. The match details live there, in the
    /// provider's own review tool.
    /// </summary>
    public string? ScreeningCaseId { get; set; }

    /// <summary>When the application was submitted, in UTC.</summary>
    public DateTime SubmittedAtUtc { get; set; }
}

/// <summary>
/// That an application exists, and whether it is restricted, from the
/// <c>ApplicationDirectory</c> view. It holds no personal data.
/// </summary>
public sealed class DirectoryEntry
{
    /// <summary>The application's unique identifier.</summary>
    public Guid ApplicationId { get; set; }

    /// <summary>The applicant's market.</summary>
    public string Market { get; set; } = string.Empty;

    /// <summary>Whether erasure was already requested.</summary>
    public bool IsRestricted { get; set; }
}

/// <summary>A staff decision as the Review API records it.</summary>
public sealed class DecisionRecord
{
    /// <summary>Unique identifier of the decision.</summary>
    public Guid Id { get; set; }

    /// <summary>The application decided on.</summary>
    public Guid ApplicationId { get; set; }

    /// <summary>What was decided.</summary>
    public StaffDecisionKind Kind { get; set; }

    /// <summary>Who decided.</summary>
    public string DecidedBy { get; set; } = string.Empty;

    /// <summary>The staff member's note, if any.</summary>
    public string? Note { get; set; }

    /// <summary>When it was decided, in UTC.</summary>
    public DateTime DecidedAtUtc { get; set; }
}

/// <summary>An entry the Review API adds to the access log.</summary>
public sealed class AccessRecord
{
    /// <summary>Unique identifier of the entry.</summary>
    public Guid Id { get; set; }

    /// <summary>Whose data was accessed.</summary>
    public Guid ApplicationId { get; set; }

    /// <summary>Who made the access.</summary>
    public string Actor { get; set; } = string.Empty;

    /// <summary>What was done.</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>When, in UTC.</summary>
    public DateTime OccurredAtUtc { get; set; }
}

/// <summary>
/// The Review API's view of one market's onboarding database. The database grants decide what
/// it can do: read the review queue, add decisions, queue work, add to the access log.
/// </summary>
public sealed class ReviewDbContext : DbContext
{
    /// <summary>Creates a context for one market's database.</summary>
    public ReviewDbContext(DbContextOptions<ReviewDbContext> options) : base(options)
    {
    }

    /// <summary>Cases waiting for staff.</summary>
    public DbSet<ReviewCase> Cases => Set<ReviewCase>();

    /// <summary>Every application, without personal data.</summary>
    public DbSet<DirectoryEntry> Directory => Set<DirectoryEntry>();

    /// <summary>Decisions recorded by staff.</summary>
    public DbSet<DecisionRecord> Decisions => Set<DecisionRecord>();

    /// <summary>The access log, which can only be added to.</summary>
    public DbSet<AccessRecord> AccessLog => Set<AccessRecord>();

    /// <inheritdoc />
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcConverter>();
    }

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ReviewCase>(entity =>
        {
            entity.HasNoKey();
            entity.ToView("ReviewQueue");
        });

        modelBuilder.Entity<DirectoryEntry>(entity =>
        {
            entity.HasNoKey();
            entity.ToView("ApplicationDirectory");
        });

        modelBuilder.Entity<DecisionRecord>(entity =>
        {
            entity.ToTable("ReviewDecisions");
            entity.HasKey(d => d.Id);
            entity.Property(d => d.Id).ValueGeneratedNever();
            entity.Property(d => d.Kind).HasConversion<int>();
        });

        modelBuilder.Entity<AccessRecord>(entity =>
        {
            entity.ToTable("AccessLog");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
        });
    }

    private sealed class UtcConverter : ValueConverter<DateTime, DateTime>
    {
        public UtcConverter()
            : base(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc))
        {
        }
    }
}
