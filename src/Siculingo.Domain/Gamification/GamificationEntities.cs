namespace Siculingo.Domain.Gamification;

public enum XpEventType
{
    LessonFirstCompletion = 1,
    CorrectExercises = 2,
    PerfectLessonBonus = 3,
    FirstActivityOfDayBonus = 4,
    ReviewCompletion = 5,
    DailyMissionBonus = 6,
    WeeklyMissionBonus = 7
}

public enum AchievementCategory
{
    Learning = 1,
    Consistency = 2,
    Precision = 3,
    Exploration = 4
}

public enum AchievementRarity
{
    Common = 1,
    Rare = 2,
    Epic = 3,
    Legendary = 4
}

public sealed class XpLedgerEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public XpEventType EventType { get; set; }
    public Guid? SourceAttemptId { get; set; }
    public Guid? SourceLessonId { get; set; }
    public int Amount { get; set; }
    public DateOnly LocalActivityDate { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public required string IdempotencyKey { get; set; }
}

public sealed class LevelThreshold
{
    public int Level { get; set; }
    public int MinXp { get; set; }
    public required string Title { get; set; }
}

public sealed class UserStats
{
    public Guid UserId { get; set; }
    public int TotalXp { get; set; }
    public int CurrentLevel { get; set; } = 1;
    public int UniqueLessonsCompleted { get; set; }
    public int TotalLessonCompletions { get; set; }
    public int PerfectLessonsCount { get; set; }
    public int ConsecutiveCorrectAnswers { get; set; }
    public int BestConsecutiveCorrectAnswers { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

public sealed class UserStreak
{
    public Guid UserId { get; set; }
    public string TimeZoneId { get; set; } = GamificationDefaults.DefaultTimeZoneId;
    public int CurrentStreak { get; set; }
    public int BestStreak { get; set; }
    public DateOnly? LastActivityDateLocal { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

public sealed class AchievementDefinition
{
    public Guid Id { get; set; }
    public required string Code { get; set; }
    public required string Name { get; set; }
    public required string Description { get; set; }
    public AchievementCategory Category { get; set; }
    public AchievementRarity Rarity { get; set; }
    public int Threshold { get; set; }
    public required string IconPath { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class UserAchievement
{
    public Guid UserId { get; set; }
    public Guid AchievementId { get; set; }
    public Guid? SourceAttemptId { get; set; }
    public DateTimeOffset UnlockedAtUtc { get; set; }
}

public sealed class UserHearts
{
    public Guid UserId { get; set; }
    public int CurrentHearts { get; set; } = GamificationDefaults.MaxHearts;
    public DateTimeOffset? RegenerationAnchorUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

public enum MissionScope
{
    Daily = 1,
    Weekly = 2
}

public sealed record MissionTemplate(
    string Code,
    MissionScope Scope,
    string Title,
    string Description,
    string IconPath,
    int TargetValue,
    int RewardXp,
    int SortOrder);

public sealed class UserMissionProgress
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public MissionScope Scope { get; set; }
    public required string MissionCode { get; set; }
    public DateOnly PeriodStartLocal { get; set; }
    public int CurrentValue { get; set; }
    public int TargetValue { get; set; }
    public int RewardXp { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public Guid? RewardLedgerEntryId { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}

public static class GamificationDefaults
{
    public const string DefaultTimeZoneId = "Europe/Rome";
    public const int CorrectExerciseXp = 2;
    public const int FirstLessonCompletionXp = 10;
    public const int PerfectLessonBonusXp = 5;
    public const int FirstActivityOfDayBonusXp = 5;
    public const int ReviewCompletionXp = 5;
    public const int DailyRewardedReviewCap = 3;
    public const int MaxHearts = 5;
    public const int HeartRegenerationHours = 4;
    public const int DailyMissionRewardXp = 10;
    public const int WeeklyMissionRewardXp = 25;

    public static class MissionCodes
    {
        public const string DailyLessonOne = "daily-lesson-1";
        public const string DailyXpTwentyFive = "daily-xp-25";
        public const string DailyCorrectTen = "daily-correct-10";
        public const string WeeklyLessonsFour = "weekly-lessons-4";
        public const string WeeklyPerfectTwo = "weekly-perfect-2";
    }

    public static IReadOnlyList<MissionTemplate> MissionCatalog { get; } =
    [
        new(
            MissionCodes.DailyLessonOne,
            MissionScope.Daily,
            "Passo del giorno",
            "Completa 1 lezione o sessione di pratica oggi.",
            "/assets/icons/book.png",
            1,
            DailyMissionRewardXp,
            1),
        new(
            MissionCodes.DailyXpTwentyFive,
            MissionScope.Daily,
            "Riscaldamento catanese",
            "Guadagna 25 XP dalle lezioni di oggi.",
            "/assets/badges/xp.png",
            25,
            DailyMissionRewardXp,
            2),
        new(
            MissionCodes.DailyCorrectTen,
            MissionScope.Daily,
            "Risposte sicure",
            "Rispondi correttamente a 10 esercizi oggi.",
            "/assets/icons/goal.png",
            10,
            DailyMissionRewardXp,
            3),
        new(
            MissionCodes.WeeklyLessonsFour,
            MissionScope.Weekly,
            "Settimana sul sentiero",
            "Completa 4 lezioni o sessioni di pratica questa settimana.",
            "/assets/icons/flame.png",
            4,
            WeeklyMissionRewardXp,
            4),
        new(
            MissionCodes.WeeklyPerfectTwo,
            MissionScope.Weekly,
            "Precisione dell'Etna",
            "Concludi 2 lezioni senza errori questa settimana.",
            "/assets/badges/star.png",
            2,
            WeeklyMissionRewardXp,
            5)
    ];

    public static class AchievementCodes
    {
        public const string FirstLesson = "first-lesson";
        public const string PerfectLesson = "perfect-lesson";
        public const string PrecisionTen = "precision-10";
        public const string StreakThree = "streak-3";
        public const string StreakSeven = "streak-7";
        public const string PilotUnitComplete = "pilot-unit-complete";
    }

    public static IReadOnlyList<LevelThreshold> SeedLevelThresholds() =>
    [
        new() { Level = 1, MinXp = 0, Title = "Curioso" },
        new() { Level = 2, MinXp = 100, Title = "Ospite al bar" },
        new() { Level = 3, MinXp = 250, Title = "Amico di piazza" },
        new() { Level = 4, MinXp = 450, Title = "Chiacchierone" },
        new() { Level = 5, MinXp = 700, Title = "Mbare di fiducia" },
        new() { Level = 6, MinXp = 1000, Title = "Voce dell'Etna" },
        new() { Level = 7, MinXp = 1400, Title = "Catanese doc" },
        new() { Level = 8, MinXp = 1900, Title = "Maestro di curtigghiu" }
    ];

    public static IReadOnlyList<AchievementDefinition> SeedAchievements() =>
    [
        new()
        {
            Id = Guid.Parse("a1000000-0000-0000-0000-000000000001"),
            Code = AchievementCodes.FirstLesson,
            Name = "Il primo passo",
            Description = "Completa la tua prima lezione sul sentiero.",
            Category = AchievementCategory.Learning,
            Rarity = AchievementRarity.Common,
            Threshold = 1,
            IconPath = "/assets/badges/check.png",
            SortOrder = 1,
            IsActive = true
        },
        new()
        {
            Id = Guid.Parse("a1000000-0000-0000-0000-000000000002"),
            Code = AchievementCodes.PerfectLesson,
            Name = "Senza sbavature",
            Description = "Concludi una lezione rispondendo bene a tutti gli esercizi.",
            Category = AchievementCategory.Precision,
            Rarity = AchievementRarity.Common,
            Threshold = 1,
            IconPath = "/assets/badges/star.png",
            SortOrder = 2,
            IsActive = true
        },
        new()
        {
            Id = Guid.Parse("a1000000-0000-0000-0000-000000000003"),
            Code = AchievementCodes.PrecisionTen,
            Name = "Colpo sicuro",
            Description = "Raggiungi 10 risposte corrette consecutive.",
            Category = AchievementCategory.Precision,
            Rarity = AchievementRarity.Rare,
            Threshold = 10,
            IconPath = "/assets/icons/goal.png",
            SortOrder = 3,
            IsActive = true
        },
        new()
        {
            Id = Guid.Parse("a1000000-0000-0000-0000-000000000004"),
            Code = AchievementCodes.StreakThree,
            Name = "Tre giorni di fila",
            Description = "Mantieni attiva la serie per 3 giorni consecutivi.",
            Category = AchievementCategory.Consistency,
            Rarity = AchievementRarity.Rare,
            Threshold = 3,
            IconPath = "/assets/icons/flame.png",
            SortOrder = 4,
            IsActive = true
        },
        new()
        {
            Id = Guid.Parse("a1000000-0000-0000-0000-000000000005"),
            Code = AchievementCodes.StreakSeven,
            Name = "Settimana catanese",
            Description = "Studia per 7 giorni consecutivi senza spezzare la serie.",
            Category = AchievementCategory.Consistency,
            Rarity = AchievementRarity.Epic,
            Threshold = 7,
            IconPath = "/assets/badges/shield.png",
            SortOrder = 5,
            IsActive = true
        },
        new()
        {
            Id = Guid.Parse("a1000000-0000-0000-0000-000000000006"),
            Code = AchievementCodes.PilotUnitComplete,
            Name = "Pronto a salutare",
            Description = "Completa tutte le lezioni di un'unità pubblicata.",
            Category = AchievementCategory.Exploration,
            Rarity = AchievementRarity.Epic,
            Threshold = 5,
            IconPath = "/assets/icons/trophy.png",
            SortOrder = 6,
            IsActive = true
        }
    ];
}
