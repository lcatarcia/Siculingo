using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Siculingo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class GamificationLedgerLevelsStreaksAchievements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Achievements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false, collation: "Latin1_General_100_CI_AS"),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Category = table.Column<int>(type: "int", nullable: false),
                    Rarity = table.Column<int>(type: "int", nullable: false),
                    Threshold = table.Column<int>(type: "int", nullable: false),
                    IconPath = table.Column<string>(type: "varchar(160)", unicode: false, maxLength: 160, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Achievements", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LevelThresholds",
                columns: table => new
                {
                    Level = table.Column<int>(type: "int", nullable: false),
                    MinXp = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LevelThresholds", x => x.Level);
                });

            migrationBuilder.CreateTable(
                name: "UserStats",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TotalXp = table.Column<int>(type: "int", nullable: false),
                    CurrentLevel = table.Column<int>(type: "int", nullable: false),
                    UniqueLessonsCompleted = table.Column<int>(type: "int", nullable: false),
                    TotalLessonCompletions = table.Column<int>(type: "int", nullable: false),
                    PerfectLessonsCount = table.Column<int>(type: "int", nullable: false),
                    ConsecutiveCorrectAnswers = table.Column<int>(type: "int", nullable: false),
                    BestConsecutiveCorrectAnswers = table.Column<int>(type: "int", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserStats", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_UserStats_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserStreaks",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TimeZoneId = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    CurrentStreak = table.Column<int>(type: "int", nullable: false),
                    BestStreak = table.Column<int>(type: "int", nullable: false),
                    LastActivityDateLocal = table.Column<DateOnly>(type: "date", nullable: true),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserStreaks", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_UserStreaks_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "XpLedger",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventType = table.Column<int>(type: "int", nullable: false),
                    SourceAttemptId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceLessonId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Amount = table.Column<int>(type: "int", nullable: false),
                    LocalActivityDate = table.Column<DateOnly>(type: "date", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "varchar(120)", unicode: false, maxLength: 120, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_XpLedger", x => x.Id);
                    table.ForeignKey(
                        name: "FK_XpLedger_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_XpLedger_LessonAttempts_SourceAttemptId",
                        column: x => x.SourceAttemptId,
                        principalTable: "LessonAttempts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_XpLedger_Lessons_SourceLessonId",
                        column: x => x.SourceLessonId,
                        principalTable: "Lessons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UserAchievements",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AchievementId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceAttemptId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UnlockedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserAchievements", x => new { x.UserId, x.AchievementId });
                    table.ForeignKey(
                        name: "FK_UserAchievements_Achievements_AchievementId",
                        column: x => x.AchievementId,
                        principalTable: "Achievements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UserAchievements_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserAchievements_LessonAttempts_SourceAttemptId",
                        column: x => x.SourceAttemptId,
                        principalTable: "LessonAttempts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "Achievements",
                columns: new[] { "Id", "Category", "Code", "Description", "IconPath", "IsActive", "Name", "Rarity", "SortOrder", "Threshold" },
                values: new object[,]
                {
                    { new Guid("a1000000-0000-0000-0000-000000000001"), 1, "first-lesson", "Completa la tua prima lezione sul sentiero.", "/assets/badges/check.png", true, "Il primo passo", 1, 1, 1 },
                    { new Guid("a1000000-0000-0000-0000-000000000002"), 3, "perfect-lesson", "Concludi una lezione rispondendo bene a tutti gli esercizi.", "/assets/badges/star.png", true, "Senza sbavature", 1, 2, 1 },
                    { new Guid("a1000000-0000-0000-0000-000000000003"), 3, "precision-10", "Raggiungi 10 risposte corrette consecutive.", "/assets/icons/goal.png", true, "Colpo sicuro", 2, 3, 10 },
                    { new Guid("a1000000-0000-0000-0000-000000000004"), 2, "streak-3", "Mantieni attiva la serie per 3 giorni consecutivi.", "/assets/icons/flame.png", true, "Tre giorni di fila", 2, 4, 3 },
                    { new Guid("a1000000-0000-0000-0000-000000000005"), 2, "streak-7", "Studia per 7 giorni consecutivi senza spezzare la serie.", "/assets/badges/shield.png", true, "Settimana catanese", 3, 5, 7 },
                    { new Guid("a1000000-0000-0000-0000-000000000006"), 4, "pilot-unit-complete", "Completa tutte le lezioni di un'unità pubblicata.", "/assets/icons/trophy.png", true, "Pronto a salutare", 3, 6, 5 }
                });

            migrationBuilder.InsertData(
                table: "LevelThresholds",
                columns: new[] { "Level", "MinXp", "Title" },
                values: new object[,]
                {
                    { 1, 0, "Curioso" },
                    { 2, 100, "Ospite al bar" },
                    { 3, 250, "Amico di piazza" },
                    { 4, 450, "Chiacchierone" },
                    { 5, 700, "Mbare di fiducia" },
                    { 6, 1000, "Voce dell'Etna" },
                    { 7, 1400, "Catanese doc" },
                    { 8, 1900, "Maestro di curtigghiu" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Achievements_Code",
                table: "Achievements",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LevelThresholds_MinXp",
                table: "LevelThresholds",
                column: "MinXp",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserAchievements_AchievementId",
                table: "UserAchievements",
                column: "AchievementId");

            migrationBuilder.CreateIndex(
                name: "IX_UserAchievements_SourceAttemptId",
                table: "UserAchievements",
                column: "SourceAttemptId");

            migrationBuilder.CreateIndex(
                name: "IX_UserAchievements_UserId_SourceAttemptId",
                table: "UserAchievements",
                columns: new[] { "UserId", "SourceAttemptId" });

            migrationBuilder.CreateIndex(
                name: "IX_XpLedger_IdempotencyKey",
                table: "XpLedger",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_XpLedger_SourceAttemptId",
                table: "XpLedger",
                column: "SourceAttemptId");

            migrationBuilder.CreateIndex(
                name: "IX_XpLedger_SourceLessonId",
                table: "XpLedger",
                column: "SourceLessonId");

            migrationBuilder.CreateIndex(
                name: "IX_XpLedger_UserId_CreatedAtUtc",
                table: "XpLedger",
                columns: new[] { "UserId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_XpLedger_UserId_LocalActivityDate_EventType",
                table: "XpLedger",
                columns: new[] { "UserId", "LocalActivityDate", "EventType" });

            migrationBuilder.CreateIndex(
                name: "IX_XpLedger_UserId_SourceAttemptId",
                table: "XpLedger",
                columns: new[] { "UserId", "SourceAttemptId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LevelThresholds");

            migrationBuilder.DropTable(
                name: "UserAchievements");

            migrationBuilder.DropTable(
                name: "UserStats");

            migrationBuilder.DropTable(
                name: "UserStreaks");

            migrationBuilder.DropTable(
                name: "XpLedger");

            migrationBuilder.DropTable(
                name: "Achievements");
        }
    }
}
