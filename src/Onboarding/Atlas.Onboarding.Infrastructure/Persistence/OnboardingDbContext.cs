using Atlas.Onboarding.Domain.Applications;
using Atlas.Onboarding.Infrastructure.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Atlas.Onboarding.Infrastructure.Persistence;

/// <summary>
/// One market's onboarding database. Each market has its own, so that personal data stays in the
/// applicant's country.
/// </summary>
public sealed class OnboardingDbContext : DbContext
{
    /// <summary>Creates a context for one market's database.</summary>
    /// <param name="options">The options, including that market's connection.</param>
    public OnboardingDbContext(DbContextOptions<OnboardingDbContext> options) : base(options)
    {
    }

    /// <summary>The applications of this market.</summary>
    public DbSet<Application> Applications => Set<Application>();

    /// <summary>The images submitted with applications.</summary>
    public DbSet<ApplicationDocument> Documents => Set<ApplicationDocument>();

    /// <summary>The work waiting to be done on applications.</summary>
    public DbSet<ProcessingJob> Jobs => Set<ProcessingJob>();

    /// <summary>Decisions by staff, recorded by the Review API.</summary>
    public DbSet<StaffDecision> StaffDecisions => Set<StaffDecision>();

    /// <summary>The record of every access to applicants' data.</summary>
    public DbSet<AccessLogEntry> AccessLog => Set<AccessLogEntry>();

    /// <inheritdoc />
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // SQL Server returns unspecified date kinds; everything we store is UTC.
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcConverter>();
    }

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Application>(entity =>
        {
            entity.ToTable("Applications");
            entity.HasKey(a => a.Id);
            entity.Property(a => a.Id).ValueGeneratedNever();
            entity.Property(a => a.Market).HasMaxLength(8).IsRequired();
            entity.Property(a => a.Status).HasConversion<int>();
            entity.Property(a => a.RejectionReason).HasMaxLength(64);
            entity.Property(a => a.FirstName).HasMaxLength(100).IsRequired();
            entity.Property(a => a.LastName).HasMaxLength(100).IsRequired();
            entity.Property(a => a.Nationality).HasMaxLength(8).IsRequired();
            entity.Property(a => a.NationalId).HasMaxLength(32);
            entity.Property(a => a.PassportNumber).HasMaxLength(32);
            entity.Property(a => a.Email).HasMaxLength(254).IsRequired();
            entity.Property(a => a.Phone).HasMaxLength(32).IsRequired();
            entity.Property(a => a.ApplicantHash).HasMaxLength(32).IsRequired();
            entity.Property(a => a.IdempotencyKey).HasMaxLength(128).IsRequired();
            entity.Property(a => a.RequestFingerprint).HasMaxLength(32).IsRequired();
            entity.Property(a => a.DocumentResult).HasConversion<int?>();
            entity.Property(a => a.ScreeningStatus).HasConversion<int?>();
            entity.Property(a => a.IdentificationId).HasMaxLength(64);
            entity.Property(a => a.ScreeningCaseId).HasMaxLength(64);
            entity.Property(a => a.AccountId).HasMaxLength(64);
            entity.Property(a => a.CardOrderId).HasMaxLength(64);
            entity.Property(a => a.RowVersion).IsRowVersion();

            // A request is only ever processed once.
            entity.HasIndex(a => a.IdempotencyKey).IsUnique();

            // At most one live application per applicant. Rejected (5) and expired (6)
            // applicants may apply again. The database enforces this, so that two replicas
            // racing cannot both succeed. A filtered index cannot use NOT, hence the form. The
            // numbers are the ApplicationStatus values; a test guards them.
            entity.HasIndex(a => a.ApplicantHash).IsUnique()
                .HasFilter("[Status] <> 5 AND [Status] <> 6");

            entity.HasMany(a => a.Documents).WithOne().HasForeignKey(d => d.ApplicationId);
            entity.Navigation(a => a.Documents).HasField("_Documents")
                .UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<ApplicationDocument>(entity =>
        {
            entity.ToTable("Documents");
            entity.HasKey(d => d.Id);
            entity.Property(d => d.Id).ValueGeneratedNever();
            entity.Property(d => d.Kind).HasConversion<int>();
            entity.Property(d => d.BlobName).HasMaxLength(200).IsRequired();
            entity.Property(d => d.Sha256).HasMaxLength(32).IsRequired();
        });

        modelBuilder.Entity<ProcessingJob>(entity =>
        {
            entity.ToTable("Jobs");
            entity.HasKey(j => j.ApplicationId);
            entity.Property(j => j.ApplicationId).ValueGeneratedNever();
            entity.Property(j => j.LockedBy).HasMaxLength(64);
            entity.Property(j => j.LastError).HasMaxLength(200);
            entity.HasIndex(j => j.NextAttemptAtUtc);
        });

        modelBuilder.Entity<StaffDecision>(entity =>
        {
            entity.ToTable("ReviewDecisions");
            entity.HasKey(d => d.Id);
            entity.Property(d => d.Id).ValueGeneratedNever();
            entity.Property(d => d.Kind).HasConversion<int>();
            entity.Property(d => d.DecidedBy).HasMaxLength(128).IsRequired();
            entity.Property(d => d.Note).HasMaxLength(1000);
            entity.Property(d => d.Outcome).HasMaxLength(32);

            // One review decision and one branch activation per application, however many
            // times an officer presses the button. The values are StaffDecisionKind. Both
            // indexes are on the same column, so they must be named or EF merges them into one.
            entity.HasIndex(d => d.ApplicationId, "IX_ReviewDecisions_Review").IsUnique()
                .HasFilter("[Kind] <= 1");
            entity.HasIndex(d => d.ApplicationId, "IX_ReviewDecisions_Branch").IsUnique()
                .HasFilter("[Kind] = 2");
        });

        modelBuilder.Entity<AccessLogEntry>(entity =>
        {
            entity.ToTable("AccessLog");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.Actor).HasMaxLength(160).IsRequired();
            entity.Property(e => e.Action).HasMaxLength(64).IsRequired();
            entity.HasIndex(e => e.ApplicationId);
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
