using FpaiConnect.Domain.Common;
using FpaiConnect.Domain.Entities;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace FpaiConnect.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<AppUser, AppRole, Guid>(options)
{
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    public DbSet<Club> Clubs => Set<Club>();
    public DbSet<Player> Players => Set<Player>();
    public DbSet<Vendor> Vendors => Set<Vendor>();

    public DbSet<WelfareCase> WelfareCases => Set<WelfareCase>();
    public DbSet<WelfareCaseNote> WelfareCaseNotes => Set<WelfareCaseNote>();

    public DbSet<LegalCase> LegalCases => Set<LegalCase>();
    public DbSet<LegalCaseEvent> LegalCaseEvents => Set<LegalCaseEvent>();

    public DbSet<Voucher> Vouchers => Set<Voucher>();
    public DbSet<Expense> Expenses => Set<Expense>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<AccountantQuery> AccountantQueries => Set<AccountantQuery>();
    public DbSet<LedgerEntry> LedgerEntries => Set<LedgerEntry>();

    public DbSet<Meeting> Meetings => Set<Meeting>();
    public DbSet<MeetingAttendee> MeetingAttendees => Set<MeetingAttendee>();
    public DbSet<Motion> Motions => Set<Motion>();
    public DbSet<Vote> Votes => Set<Vote>();

    public DbSet<Event> Events => Set<Event>();
    public DbSet<EventParticipant> EventParticipants => Set<EventParticipant>();

    public DbSet<Document> Documents => Set<Document>();
    public DbSet<WorkTask> WorkTasks => Set<WorkTask>();
    public DbSet<ApprovalRequest> ApprovalRequests => Set<ApprovalRequest>();
    public DbSet<Notification> Notifications => Set<Notification>();

    /// <summary>
    /// Users excluding soft-deleted accounts. Use this anywhere a user is being listed,
    /// looked up or validated; use <see cref="Users"/> only when resolving historical authorship.
    /// </summary>
    public IQueryable<AppUser> ActiveUsers => Users.Where(u => !u.IsDeleted);

    /// <summary>
    /// Every timestamp in this system is UTC. SQLite hands values back as Unspecified and
    /// SQL Server as Unspecified too, which would serialise without a 'Z' and shift dates in
    /// the browser — so normalise on write and re-stamp the kind on read.
    /// </summary>
    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        base.ConfigureConventions(builder);

        builder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        builder.Properties<DateTime?>().HaveConversion<NullableUtcDateTimeConverter>();
    }

    private sealed class UtcDateTimeConverter()
        : Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTime, DateTime>(
            v => v.Kind == DateTimeKind.Utc ? v : v.ToUniversalTime(),
            v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

    private sealed class NullableUtcDateTimeConverter()
        : Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTime?, DateTime?>(
            v => v.HasValue ? (v.Value.Kind == DateTimeKind.Utc ? v : v.Value.ToUniversalTime()) : v,
            v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v);

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        b.Entity<AppUser>(e =>
        {
            e.ToTable("Users");
            e.Property(x => x.FullName).HasMaxLength(200).IsRequired();
            e.Property(x => x.JobTitle).HasMaxLength(150);
            e.Property(x => x.GoogleSubjectId).HasMaxLength(100);
            e.HasIndex(x => x.GoogleSubjectId).IsUnique().HasFilter(null);
            e.Property(x => x.MicrosoftSubjectId).HasMaxLength(100);
            e.HasIndex(x => x.MicrosoftSubjectId).IsUnique().HasFilter(null);
            e.Property(x => x.ColorScheme).HasMaxLength(40).IsRequired();
            e.Property(x => x.FontChoice).HasMaxLength(40).IsRequired();
            e.Property(x => x.ApprovalNote).HasMaxLength(1000);
            e.Property(x => x.RegistrationNote).HasMaxLength(1000);
            // Approvers are users too; no cascade, so removing an approver never removes accounts.
            e.HasOne(x => x.ApprovedBy).WithMany()
                .HasForeignKey(x => x.ApprovedById).OnDelete(DeleteBehavior.NoAction);
            // The pending queue is read on every admin visit.
            e.HasIndex(x => x.Status);
            e.HasOne(x => x.Department).WithMany(d => d.Users)
                .HasForeignKey(x => x.DepartmentId).OnDelete(DeleteBehavior.SetNull);
            // Deliberately NOT globally filtered on IsDeleted. Votes, meeting attendance and
            // record authorship reference users as a required end; a global filter would drop
            // those rows and silently change historical vote counts. Callers that list or
            // select users filter explicitly instead — see AppDbContext.ActiveUsers.
        });
        b.Entity<AppRole>().ToTable("Roles");
        b.Entity<Microsoft.AspNetCore.Identity.IdentityUserRole<Guid>>().ToTable("UserRoles");
        b.Entity<Microsoft.AspNetCore.Identity.IdentityUserClaim<Guid>>().ToTable("UserClaims");
        b.Entity<Microsoft.AspNetCore.Identity.IdentityUserLogin<Guid>>().ToTable("UserLogins");
        b.Entity<Microsoft.AspNetCore.Identity.IdentityUserToken<Guid>>().ToTable("UserTokens");
        b.Entity<Microsoft.AspNetCore.Identity.IdentityRoleClaim<Guid>>().ToTable("RoleClaims");

        b.Entity<Department>(e =>
        {
            e.Property(x => x.Code).HasMaxLength(30).IsRequired();
            e.Property(x => x.Name).HasMaxLength(120).IsRequired();
            e.HasIndex(x => x.Code).IsUnique();
        });

        b.Entity<RefreshToken>(e =>
        {
            e.Property(x => x.TokenHash).HasMaxLength(200).IsRequired();
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasOne(x => x.User).WithMany(u => u.RefreshTokens)
                .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<AuditLog>(e =>
        {
            e.Property(x => x.EntityName).HasMaxLength(120).IsRequired();
            e.Property(x => x.EntityId).HasMaxLength(60).IsRequired();
            e.Property(x => x.Action).HasMaxLength(30).IsRequired();
            e.Property(x => x.UserName).HasMaxLength(200);
            e.HasIndex(x => new { x.EntityName, x.EntityId });
            e.HasIndex(x => x.Timestamp);
        });

        b.Entity<Club>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(150).IsRequired();
            e.Property(x => x.City).HasMaxLength(100);
            e.Property(x => x.League).HasMaxLength(100);
            e.HasIndex(x => x.Name);
        });

        b.Entity<Player>(e =>
        {
            e.Property(x => x.MembershipId).HasMaxLength(40).IsRequired();
            e.Property(x => x.FullName).HasMaxLength(200).IsRequired();
            e.Property(x => x.Position).HasMaxLength(60);
            e.Property(x => x.Nationality).HasMaxLength(80);
            e.Property(x => x.ContactEmail).HasMaxLength(200);
            e.Property(x => x.ContactPhone).HasMaxLength(40);
            e.HasIndex(x => x.MembershipId).IsUnique();
            e.HasIndex(x => x.FullName);
            e.HasOne(x => x.CurrentClub).WithMany(c => c.Players)
                .HasForeignKey(x => x.CurrentClubId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<Vendor>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.GstNumber).HasMaxLength(30);
            e.Property(x => x.ContactEmail).HasMaxLength(200);
            e.Property(x => x.ContactPhone).HasMaxLength(40);
            e.Property(x => x.BankAccount).HasMaxLength(60);
            e.HasIndex(x => x.Name);
        });

        b.Entity<WelfareCase>(e =>
        {
            e.Property(x => x.CaseNumber).HasMaxLength(40).IsRequired();
            e.Property(x => x.Title).HasMaxLength(250).IsRequired();
            e.HasIndex(x => x.CaseNumber).IsUnique();
            e.HasIndex(x => new { x.Status, x.DepartmentId });
            e.HasOne(x => x.Player).WithMany(p => p.WelfareCases)
                .HasForeignKey(x => x.PlayerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Department).WithMany().HasForeignKey(x => x.DepartmentId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.AssignedOfficer).WithMany().HasForeignKey(x => x.AssignedOfficerId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<WelfareCaseNote>(e =>
        {
            e.Property(x => x.Note).IsRequired();
            e.HasOne(x => x.WelfareCase).WithMany(c => c.Notes)
                .HasForeignKey(x => x.WelfareCaseId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Author).WithMany().HasForeignKey(x => x.AuthorId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<LegalCase>(e =>
        {
            e.Property(x => x.CaseNumber).HasMaxLength(60).IsRequired();
            e.Property(x => x.Title).HasMaxLength(250).IsRequired();
            e.Property(x => x.LawyerName).HasMaxLength(150);
            e.Property(x => x.LawyerFirm).HasMaxLength(200);
            e.Property(x => x.Currency).HasMaxLength(3);
            e.Property(x => x.ClaimAmount).HasPrecision(18, 2);
            e.Property(x => x.AwardedAmount).HasPrecision(18, 2);
            e.HasIndex(x => x.CaseNumber).IsUnique();
            e.HasIndex(x => new { x.Status, x.Type });
            e.HasOne(x => x.Player).WithMany(p => p.LegalCases)
                .HasForeignKey(x => x.PlayerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.OpposingClub).WithMany().HasForeignKey(x => x.OpposingClubId)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Department).WithMany().HasForeignKey(x => x.DepartmentId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.AssignedCounsel).WithMany().HasForeignKey(x => x.AssignedCounselId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<LegalCaseEvent>(e =>
        {
            e.Property(x => x.Title).HasMaxLength(200).IsRequired();
            e.HasOne(x => x.LegalCase).WithMany(c => c.Events)
                .HasForeignKey(x => x.LegalCaseId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Author).WithMany().HasForeignKey(x => x.AuthorId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<Voucher>(e =>
        {
            e.Property(x => x.VoucherNumber).HasMaxLength(40).IsRequired();
            e.Property(x => x.Currency).HasMaxLength(3);
            e.Property(x => x.Amount).HasPrecision(18, 2);
            e.Property(x => x.TaxAmount).HasPrecision(18, 2);
            e.Property(x => x.TotalAmount).HasPrecision(18, 2);
            e.HasIndex(x => x.VoucherNumber).IsUnique();
            e.HasIndex(x => new { x.Status, x.DepartmentId });
            e.HasOne(x => x.Vendor).WithMany(v => v.Vouchers)
                .HasForeignKey(x => x.VendorId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Department).WithMany().HasForeignKey(x => x.DepartmentId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.ApprovedBy).WithMany().HasForeignKey(x => x.ApprovedById)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.ReconciledBy).WithMany().HasForeignKey(x => x.ReconciledById)
                .OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<Expense>(e =>
        {
            e.Property(x => x.ExpenseNumber).HasMaxLength(40).IsRequired();
            e.Property(x => x.Title).HasMaxLength(250).IsRequired();
            e.Property(x => x.Category).HasMaxLength(100);
            e.Property(x => x.Currency).HasMaxLength(3);
            e.Property(x => x.Amount).HasPrecision(18, 2);
            e.HasIndex(x => x.ExpenseNumber).IsUnique();
            e.HasIndex(x => new { x.Status, x.DepartmentId });
            e.HasOne(x => x.Department).WithMany().HasForeignKey(x => x.DepartmentId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.SubmittedBy).WithMany().HasForeignKey(x => x.SubmittedById)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.ApprovedBy).WithMany().HasForeignKey(x => x.ApprovedById)
                .OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<Invoice>(e =>
        {
            e.Property(x => x.InvoiceNumber).HasMaxLength(60).IsRequired();
            e.Property(x => x.Currency).HasMaxLength(3);
            e.Property(x => x.Amount).HasPrecision(18, 2);
            e.Property(x => x.TaxAmount).HasPrecision(18, 2);
            e.HasIndex(x => x.InvoiceNumber);
            e.HasOne(x => x.Vendor).WithMany().HasForeignKey(x => x.VendorId)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Expense).WithMany(x => x.Invoices).HasForeignKey(x => x.ExpenseId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<AccountantQuery>(e =>
        {
            e.Property(x => x.Question).IsRequired();
            e.HasOne(x => x.Voucher).WithMany(v => v.Queries).HasForeignKey(x => x.VoucherId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Expense).WithMany(v => v.Queries).HasForeignKey(x => x.ExpenseId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.RaisedBy).WithMany().HasForeignKey(x => x.RaisedById)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.AnsweredBy).WithMany().HasForeignKey(x => x.AnsweredById)
                .OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<LedgerEntry>(e =>
        {
            e.Property(x => x.Amount).HasPrecision(18, 2);
            e.Property(x => x.BudgetedAmount).HasPrecision(18, 2);
            e.HasIndex(x => new { x.FiscalYear, x.Month, x.DepartmentId });
            e.HasOne(x => x.Department).WithMany().HasForeignKey(x => x.DepartmentId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Meeting>(e =>
        {
            e.Property(x => x.ReferenceNumber).HasMaxLength(40).IsRequired();
            e.Property(x => x.Title).HasMaxLength(250).IsRequired();
            e.Property(x => x.Location).HasMaxLength(200);
            e.Property(x => x.VideoLink).HasMaxLength(500);
            e.HasIndex(x => x.ReferenceNumber).IsUnique();
            e.HasIndex(x => x.ScheduledAt);
            e.HasOne(x => x.Department).WithMany().HasForeignKey(x => x.DepartmentId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Chair).WithMany().HasForeignKey(x => x.ChairId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<MeetingAttendee>(e =>
        {
            e.HasIndex(x => new { x.MeetingId, x.UserId }).IsUnique();
            e.HasOne(x => x.Meeting).WithMany(m => m.Attendees)
                .HasForeignKey(x => x.MeetingId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Motion>(e =>
        {
            e.Property(x => x.Title).HasMaxLength(250).IsRequired();
            e.HasOne(x => x.Meeting).WithMany(m => m.Motions)
                .HasForeignKey(x => x.MeetingId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Vote>(e =>
        {
            // One vote per user per motion, enforced at the database level.
            e.HasIndex(x => new { x.MotionId, x.UserId }).IsUnique();
            e.HasOne(x => x.Motion).WithMany(m => m.Votes)
                .HasForeignKey(x => x.MotionId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Event>(e =>
        {
            e.Property(x => x.ReferenceNumber).HasMaxLength(40).IsRequired();
            e.Property(x => x.Name).HasMaxLength(250).IsRequired();
            e.Property(x => x.Venue).HasMaxLength(200);
            e.Property(x => x.City).HasMaxLength(100);
            e.Property(x => x.BudgetAmount).HasPrecision(18, 2);
            e.Property(x => x.ActualCost).HasPrecision(18, 2);
            e.HasIndex(x => x.ReferenceNumber).IsUnique();
            e.HasIndex(x => x.StartDate);
            e.HasOne(x => x.Department).WithMany().HasForeignKey(x => x.DepartmentId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Owner).WithMany().HasForeignKey(x => x.OwnerId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<EventParticipant>(e =>
        {
            e.HasIndex(x => new { x.EventId, x.PlayerId }).IsUnique();
            e.HasOne(x => x.Event).WithMany(m => m.Participants)
                .HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Player).WithMany().HasForeignKey(x => x.PlayerId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Document>(e =>
        {
            e.Property(x => x.Title).HasMaxLength(250).IsRequired();
            e.Property(x => x.FileName).HasMaxLength(260).IsRequired();
            e.Property(x => x.ContentType).HasMaxLength(150);
            e.Property(x => x.StoragePath).HasMaxLength(500).IsRequired();
            e.Property(x => x.Sha256).HasMaxLength(64);
            e.HasIndex(x => x.Category);
            e.HasOne(x => x.Department).WithMany().HasForeignKey(x => x.DepartmentId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.UploadedBy).WithMany().HasForeignKey(x => x.UploadedById)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.WelfareCase).WithMany(c => c.Documents).HasForeignKey(x => x.WelfareCaseId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.LegalCase).WithMany(c => c.Documents).HasForeignKey(x => x.LegalCaseId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Voucher).WithMany(c => c.Documents).HasForeignKey(x => x.VoucherId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Expense).WithMany(c => c.Documents).HasForeignKey(x => x.ExpenseId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Meeting).WithMany(c => c.Documents).HasForeignKey(x => x.MeetingId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Event).WithMany(c => c.Documents).HasForeignKey(x => x.EventId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<WorkTask>(e =>
        {
            e.Property(x => x.ReferenceNumber).HasMaxLength(40).IsRequired();
            e.Property(x => x.Title).HasMaxLength(250).IsRequired();
            e.Property(x => x.RelatedEntityType).HasMaxLength(60);
            e.HasIndex(x => x.ReferenceNumber).IsUnique();
            e.HasIndex(x => new { x.Status, x.AssigneeId });
            e.HasOne(x => x.Department).WithMany().HasForeignKey(x => x.DepartmentId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Assignee).WithMany().HasForeignKey(x => x.AssigneeId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<ApprovalRequest>(e =>
        {
            e.Property(x => x.ReferenceNumber).HasMaxLength(40).IsRequired();
            e.Property(x => x.Title).HasMaxLength(250).IsRequired();
            e.Property(x => x.EntityType).HasMaxLength(60).IsRequired();
            e.Property(x => x.Amount).HasPrecision(18, 2);
            e.HasIndex(x => x.ReferenceNumber).IsUnique();
            e.HasIndex(x => new { x.Status, x.DepartmentId });
            e.HasIndex(x => new { x.EntityType, x.EntityId });
            e.HasOne(x => x.Department).WithMany().HasForeignKey(x => x.DepartmentId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.RequestedBy).WithMany().HasForeignKey(x => x.RequestedById)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.DecidedBy).WithMany().HasForeignKey(x => x.DecidedById)
                .OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<Notification>(e =>
        {
            e.Property(x => x.Title).HasMaxLength(250).IsRequired();
            e.Property(x => x.Link).HasMaxLength(500);
            e.HasIndex(x => new { x.UserId, x.IsRead });
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        ApplySoftDeleteFilters(b);
    }

    /// <summary>
    /// Adds `!IsDeleted` to every BaseEntity-derived type so callers can never accidentally
    /// read soft-deleted rows. AppUser gets its filter in its own configuration above.
    /// </summary>
    private static void ApplySoftDeleteFilters(ModelBuilder b)
    {
        foreach (var entityType in b.Model.GetEntityTypes())
        {
            if (!typeof(BaseEntity).IsAssignableFrom(entityType.ClrType)) continue;

            var param = Expression.Parameter(entityType.ClrType, "e");
            var body = Expression.Not(Expression.Property(param, nameof(BaseEntity.IsDeleted)));
            b.Entity(entityType.ClrType).HasQueryFilter(Expression.Lambda(body, param));
        }
    }
}
