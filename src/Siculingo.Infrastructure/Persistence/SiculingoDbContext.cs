using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Siculingo.Domain.Administration;
using Siculingo.Domain.Gamification;
using Siculingo.Domain.Learning;
using Siculingo.Domain.Social;
using Siculingo.Infrastructure.Accounts;

namespace Siculingo.Infrastructure.Persistence;

public sealed class SiculingoDbContext(DbContextOptions<SiculingoDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<Course> Courses => Set<Course>();
    public DbSet<CourseSection> CourseSections => Set<CourseSection>();
    public DbSet<LearningUnit> LearningUnits => Set<LearningUnit>();
    public DbSet<Lesson> Lessons => Set<Lesson>();
    public DbSet<LessonAttempt> LessonAttempts => Set<LessonAttempt>();
    public DbSet<LessonAttemptAnswer> LessonAttemptAnswers => Set<LessonAttemptAnswer>();
    public DbSet<UserLessonCompletion> UserLessonCompletions => Set<UserLessonCompletion>();
    public DbSet<XpLedgerEntry> XpLedger => Set<XpLedgerEntry>();
    public DbSet<LevelThreshold> LevelThresholds => Set<LevelThreshold>();
    public DbSet<UserStats> UserStats => Set<UserStats>();
    public DbSet<UserStreak> UserStreaks => Set<UserStreak>();
    public DbSet<AchievementDefinition> Achievements => Set<AchievementDefinition>();
    public DbSet<UserAchievement> UserAchievements => Set<UserAchievement>();
    public DbSet<UserHearts> UserHearts => Set<UserHearts>();
    public DbSet<UserMissionProgress> UserMissionProgress => Set<UserMissionProgress>();
    public DbSet<UserPrivacySettings> UserPrivacySettings => Set<UserPrivacySettings>();
    public DbSet<Friendship> Friendships => Set<Friendship>();
    public DbSet<UserBlock> UserBlocks => Set<UserBlock>();
    public DbSet<UserLeagueProfile> UserLeagueProfiles => Set<UserLeagueProfile>();
    public DbSet<WeeklyLeagueStanding> WeeklyLeagueStandings => Set<WeeklyLeagueStanding>();
    public DbSet<LinguisticLexeme> LinguisticLexemes => Set<LinguisticLexeme>();
    public DbSet<UserReport> UserReports => Set<UserReport>();
    public DbSet<AdminAuditEntry> AdminAuditEntries => Set<AdminAuditEntry>();
    public DbSet<AccountRecoveryCode> AccountRecoveryCodes => Set<AccountRecoveryCode>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        var appUser = modelBuilder.Entity<ApplicationUser>();
        appUser.Property(x => x.DisplayName).HasMaxLength(40);
        appUser.Property(x => x.AcceptedTermsVersion).HasMaxLength(32).IsUnicode(false);
        appUser.Property(x => x.SuspensionReason).HasMaxLength(500);
        appUser.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.SuspendedByUserId).OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<IdentityRole<Guid>>().HasData(
            new IdentityRole<Guid>
            {
                Id = AdminRoles.EditorRoleId,
                Name = AdminRoles.Editor,
                NormalizedName = "EDITOR",
                ConcurrencyStamp = "8d200001-role-editor-stamp"
            },
            new IdentityRole<Guid>
            {
                Id = AdminRoles.AdminRoleId,
                Name = AdminRoles.Admin,
                NormalizedName = "ADMIN",
                ConcurrencyStamp = "8d200002-role-admin-stamp"
            });
        var course = modelBuilder.Entity<Course>();
        course.HasKey(x => x.Id);
        course.Property(x => x.Code).HasMaxLength(40).IsUnicode(false)
            .UseCollation("Latin1_General_100_CI_AS");
        course.HasIndex(x => x.Code).IsUnique();
        course.Property(x => x.Name).HasMaxLength(160);
        course.Property(x => x.SourceLanguage).HasMaxLength(8).IsUnicode(false);
        course.Property(x => x.TargetDialect).HasMaxLength(32);
        course.HasMany(x => x.Sections).WithOne().HasForeignKey(x => x.CourseId);

        var section = modelBuilder.Entity<CourseSection>();
        section.Property(x => x.Title).HasMaxLength(160);
        section.HasIndex(x => new { x.CourseId, x.SortOrder }).IsUnique();
        section.HasMany(x => x.Units).WithOne().HasForeignKey(x => x.CourseSectionId);

        var unit = modelBuilder.Entity<LearningUnit>();
        unit.Property(x => x.Title).HasMaxLength(160);
        unit.HasIndex(x => new { x.CourseSectionId, x.SortOrder }).IsUnique();
        unit.HasMany(x => x.Lessons).WithOne().HasForeignKey(x => x.LearningUnitId);

        var lesson = modelBuilder.Entity<Lesson>();
        lesson.Property(x => x.Title).HasMaxLength(160);
        lesson.Property(x => x.OrthographicVariants).HasMaxLength(500);
        lesson.Property(x => x.SourceReference).HasMaxLength(500);
        lesson.Property(x => x.ReviewerNotes).HasMaxLength(2000);
        lesson.Property(x => x.ApprovedByName).HasMaxLength(80);
        lesson.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.LastEditedByUserId).OnDelete(DeleteBehavior.Restrict);
        lesson.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ApprovedByUserId).OnDelete(DeleteBehavior.Restrict);
        lesson.HasIndex(x => new { x.LearningUnitId, x.SortOrder }).IsUnique();

        var attempt = modelBuilder.Entity<LessonAttempt>();
        attempt.HasKey(x => x.Id);
        attempt.Property(x => x.LessonSnapshotJson).IsRequired();
        attempt.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        attempt.HasOne<Lesson>().WithMany().HasForeignKey(x => x.LessonId).OnDelete(DeleteBehavior.Restrict);
        attempt.HasMany(x => x.Answers).WithOne().HasForeignKey(x => x.LessonAttemptId).OnDelete(DeleteBehavior.Cascade);
        attempt.HasIndex(x => x.UserId)
            .IsUnique()
            .HasFilter("[Status] = 0");
        attempt.HasIndex(x => new { x.UserId, x.LessonId, x.Status });

        var answer = modelBuilder.Entity<LessonAttemptAnswer>();
        answer.HasKey(x => x.Id);
        answer.Property(x => x.ExerciseId).HasMaxLength(40).IsRequired();
        answer.Property(x => x.SubmittedPayloadJson).IsRequired();
        answer.Property(x => x.Explanation).HasMaxLength(2000).IsRequired();
        answer.Property(x => x.ExpectedDisplayAnswer).HasMaxLength(2000).IsRequired();
        answer.HasIndex(x => new { x.LessonAttemptId, x.StepIndex }).IsUnique();
        answer.HasIndex(x => new { x.LessonAttemptId, x.ExerciseId }).IsUnique();

        var completion = modelBuilder.Entity<UserLessonCompletion>();
        completion.HasKey(x => new { x.UserId, x.LessonId });
        completion.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        completion.HasOne<Lesson>().WithMany().HasForeignKey(x => x.LessonId).OnDelete(DeleteBehavior.Restrict);

        var recovery = modelBuilder.Entity<AccountRecoveryCode>();
        recovery.HasKey(x => x.UserId);
        recovery.Property(x => x.CodeHash).HasMaxLength(32).IsFixedLength().IsConcurrencyToken();
        recovery.HasOne<ApplicationUser>().WithOne().HasForeignKey<AccountRecoveryCode>(x => x.UserId);

        var xpLedger = modelBuilder.Entity<XpLedgerEntry>();
        xpLedger.ToTable("XpLedger");
        xpLedger.HasKey(x => x.Id);
        xpLedger.Property(x => x.IdempotencyKey).HasMaxLength(120).IsUnicode(false).IsRequired();
        xpLedger.HasIndex(x => x.IdempotencyKey).IsUnique();
        xpLedger.HasIndex(x => new { x.UserId, x.CreatedAtUtc });
        xpLedger.HasIndex(x => new { x.UserId, x.LocalActivityDate, x.EventType });
        xpLedger.HasIndex(x => new { x.LocalActivityDate, x.UserId }).IncludeProperties(x => x.Amount);
        xpLedger.HasIndex(x => new { x.UserId, x.SourceAttemptId });
        xpLedger.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        xpLedger.HasOne<LessonAttempt>().WithMany().HasForeignKey(x => x.SourceAttemptId).OnDelete(DeleteBehavior.Restrict);
        xpLedger.HasOne<Lesson>().WithMany().HasForeignKey(x => x.SourceLessonId).OnDelete(DeleteBehavior.Restrict);

        var threshold = modelBuilder.Entity<LevelThreshold>();
        threshold.ToTable("LevelThresholds");
        threshold.HasKey(x => x.Level);
        threshold.Property(x => x.Level).ValueGeneratedNever();
        threshold.Property(x => x.Title).HasMaxLength(80).IsRequired();
        threshold.HasIndex(x => x.MinXp).IsUnique();
        threshold.HasData(GamificationDefaults.SeedLevelThresholds());

        var stats = modelBuilder.Entity<UserStats>();
        stats.ToTable("UserStats");
        stats.HasKey(x => x.UserId);
        stats.HasOne<ApplicationUser>().WithOne().HasForeignKey<UserStats>(x => x.UserId).OnDelete(DeleteBehavior.Cascade);

        var streak = modelBuilder.Entity<UserStreak>();
        streak.ToTable("UserStreaks");
        streak.HasKey(x => x.UserId);
        streak.Property(x => x.TimeZoneId).HasMaxLength(64).IsUnicode(false).IsRequired();
        streak.HasOne<ApplicationUser>().WithOne().HasForeignKey<UserStreak>(x => x.UserId).OnDelete(DeleteBehavior.Cascade);

        var achievement = modelBuilder.Entity<AchievementDefinition>();
        achievement.ToTable("Achievements");
        achievement.HasKey(x => x.Id);
        achievement.Property(x => x.Code).HasMaxLength(64).IsUnicode(false).IsRequired()
            .UseCollation("Latin1_General_100_CI_AS");
        achievement.HasIndex(x => x.Code).IsUnique();
        achievement.Property(x => x.Name).HasMaxLength(120).IsRequired();
        achievement.Property(x => x.Description).HasMaxLength(400).IsRequired();
        achievement.Property(x => x.IconPath).HasMaxLength(160).IsUnicode(false).IsRequired();
        achievement.HasData(GamificationDefaults.SeedAchievements());

        var userAchievement = modelBuilder.Entity<UserAchievement>();
        userAchievement.ToTable("UserAchievements");
        userAchievement.HasKey(x => new { x.UserId, x.AchievementId });
        userAchievement.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        userAchievement.HasOne<AchievementDefinition>().WithMany().HasForeignKey(x => x.AchievementId).OnDelete(DeleteBehavior.Restrict);
        userAchievement.HasOne<LessonAttempt>().WithMany().HasForeignKey(x => x.SourceAttemptId).OnDelete(DeleteBehavior.Restrict);
        userAchievement.HasIndex(x => new { x.UserId, x.SourceAttemptId });

        var hearts = modelBuilder.Entity<UserHearts>();
        hearts.ToTable("UserHearts", table =>
            table.HasCheckConstraint("CK_UserHearts_Range", $"[CurrentHearts] >= 0 AND [CurrentHearts] <= {GamificationDefaults.MaxHearts}"));
        hearts.HasKey(x => x.UserId);
        hearts.HasOne<ApplicationUser>().WithOne().HasForeignKey<UserHearts>(x => x.UserId).OnDelete(DeleteBehavior.Cascade);

        var mission = modelBuilder.Entity<UserMissionProgress>();
        mission.ToTable("UserMissionProgress");
        mission.HasKey(x => x.Id);
        mission.Property(x => x.MissionCode).HasMaxLength(64).IsUnicode(false).IsRequired()
            .UseCollation("Latin1_General_100_CI_AS");
        mission.HasIndex(x => new { x.UserId, x.MissionCode, x.PeriodStartLocal }).IsUnique();
        mission.HasIndex(x => new { x.UserId, x.Scope, x.PeriodStartLocal });
        mission.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        mission.HasOne<XpLedgerEntry>().WithMany().HasForeignKey(x => x.RewardLedgerEntryId).OnDelete(DeleteBehavior.Restrict);

        var privacy = modelBuilder.Entity<UserPrivacySettings>();
        privacy.ToTable("UserPrivacySettings");
        privacy.HasKey(x => x.UserId);
        privacy.HasOne<ApplicationUser>().WithOne().HasForeignKey<UserPrivacySettings>(x => x.UserId).OnDelete(DeleteBehavior.Cascade);

        var friendship = modelBuilder.Entity<Friendship>();
        friendship.ToTable("Friendships", table =>
        {
            table.HasCheckConstraint("CK_Friendships_DistinctUsers", "[UserLowId] <> [UserHighId]");
            table.HasCheckConstraint("CK_Friendships_ValidParticipants",
                "([RequesterUserId] = [UserLowId] AND [AddresseeUserId] = [UserHighId]) OR ([RequesterUserId] = [UserHighId] AND [AddresseeUserId] = [UserLowId])");
        });
        friendship.HasKey(x => x.Id);
        friendship.HasIndex(x => new { x.UserLowId, x.UserHighId }).IsUnique();
        friendship.HasIndex(x => new { x.AddresseeUserId, x.Status, x.CreatedAtUtc });
        friendship.HasIndex(x => new { x.RequesterUserId, x.Status, x.CreatedAtUtc });
        friendship.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserLowId).OnDelete(DeleteBehavior.Restrict);
        friendship.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserHighId).OnDelete(DeleteBehavior.Restrict);
        friendship.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.RequesterUserId).OnDelete(DeleteBehavior.Restrict);
        friendship.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.AddresseeUserId).OnDelete(DeleteBehavior.Restrict);

        var block = modelBuilder.Entity<UserBlock>();
        block.ToTable("UserBlocks", table =>
            table.HasCheckConstraint("CK_UserBlocks_DistinctUsers", "[BlockerUserId] <> [BlockedUserId]"));
        block.HasKey(x => new { x.BlockerUserId, x.BlockedUserId });
        block.HasIndex(x => x.BlockedUserId);
        block.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.BlockerUserId).OnDelete(DeleteBehavior.Restrict);
        block.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.BlockedUserId).OnDelete(DeleteBehavior.Restrict);

        var leagueProfile = modelBuilder.Entity<UserLeagueProfile>();
        leagueProfile.ToTable("UserLeagueProfiles");
        leagueProfile.HasKey(x => x.UserId);
        leagueProfile.HasOne<ApplicationUser>().WithOne().HasForeignKey<UserLeagueProfile>(x => x.UserId).OnDelete(DeleteBehavior.Cascade);

        var leagueStanding = modelBuilder.Entity<WeeklyLeagueStanding>();
        leagueStanding.ToTable("WeeklyLeagueStandings");
        leagueStanding.HasKey(x => new { x.WeekStartLocal, x.UserId });
        leagueStanding.HasIndex(x => new { x.WeekStartLocal, x.Tier });
        leagueStanding.HasIndex(x => new { x.Outcome, x.WeekStartLocal });
        leagueStanding.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);

        var lexeme = modelBuilder.Entity<LinguisticLexeme>();
        lexeme.ToTable("LinguisticLexemes");
        lexeme.HasKey(x => x.Id);
        lexeme.Property(x => x.Headword).HasMaxLength(120).IsRequired()
            .UseCollation("Latin1_General_100_CI_AS");
        lexeme.HasIndex(x => x.Headword);
        lexeme.Property(x => x.MeaningIt).HasMaxLength(300).IsRequired();
        lexeme.Property(x => x.UsageContext).HasMaxLength(600).IsRequired();
        lexeme.Property(x => x.OrthographicVariants).HasMaxLength(500);
        lexeme.Property(x => x.SourceReference).HasMaxLength(500);
        lexeme.Property(x => x.ReviewerNotes).HasMaxLength(2000);
        lexeme.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.LastEditedByUserId).OnDelete(DeleteBehavior.Restrict);
        lexeme.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ReviewedByUserId).OnDelete(DeleteBehavior.Restrict);

        var report = modelBuilder.Entity<UserReport>();
        report.ToTable("UserReports");
        report.HasKey(x => x.Id);
        report.Property(x => x.TargetExerciseId).HasMaxLength(40);
        report.Property(x => x.ReasonCategory).HasMaxLength(64).IsUnicode(false).IsRequired();
        report.Property(x => x.Message).HasMaxLength(1500).IsRequired();
        report.Property(x => x.ResolutionNote).HasMaxLength(1500);
        report.HasIndex(x => new { x.Status, x.CreatedAtUtc });
        report.HasIndex(x => new { x.ReporterUserId, x.CreatedAtUtc });
        report.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ReporterUserId).OnDelete(DeleteBehavior.Restrict);
        report.HasOne<Lesson>().WithMany().HasForeignKey(x => x.TargetLessonId).OnDelete(DeleteBehavior.Restrict);
        report.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.TargetUserId).OnDelete(DeleteBehavior.Restrict);
        report.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ResolvedByUserId).OnDelete(DeleteBehavior.Restrict);

        var audit = modelBuilder.Entity<AdminAuditEntry>();
        audit.ToTable("AdminAuditEntries");
        audit.HasKey(x => x.Id);
        audit.Property(x => x.ActorUsername).HasMaxLength(40).IsRequired();
        audit.Property(x => x.ActionType).HasMaxLength(80).IsUnicode(false).IsRequired();
        audit.Property(x => x.TargetKind).HasMaxLength(40).IsUnicode(false).IsRequired();
        audit.Property(x => x.TargetIdentifier).HasMaxLength(120).IsRequired();
        audit.Property(x => x.Summary).HasMaxLength(600).IsRequired();
        audit.Property(x => x.DetailsJson).HasMaxLength(4000);
        audit.HasIndex(x => x.CreatedAtUtc);
        audit.HasIndex(x => new { x.TargetKind, x.TargetIdentifier, x.CreatedAtUtc });
        audit.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
