using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Siculingo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LessonAttemptsAndPublishedSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PublishedSnapshotJson",
                table: "Lessons",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "LessonAttempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LessonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    LessonSnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ExerciseCount = table.Column<int>(type: "int", nullable: false),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    AbandonedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LessonAttempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LessonAttempts_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LessonAttempts_Lessons_LessonId",
                        column: x => x.LessonId,
                        principalTable: "Lessons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UserLessonCompletions",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LessonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FirstCompletedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastCompletedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CompletedCount = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserLessonCompletions", x => new { x.UserId, x.LessonId });
                    table.ForeignKey(
                        name: "FK_UserLessonCompletions_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserLessonCompletions_Lessons_LessonId",
                        column: x => x.LessonId,
                        principalTable: "Lessons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LessonAttemptAnswers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LessonAttemptId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StepIndex = table.Column<int>(type: "int", nullable: false),
                    ExerciseId = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    SubmittedPayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsCorrect = table.Column<bool>(type: "bit", nullable: false),
                    Explanation = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    ExpectedDisplayAnswer = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    AnsweredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LessonAttemptAnswers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LessonAttemptAnswers_LessonAttempts_LessonAttemptId",
                        column: x => x.LessonAttemptId,
                        principalTable: "LessonAttempts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LessonAttemptAnswers_LessonAttemptId_ExerciseId",
                table: "LessonAttemptAnswers",
                columns: new[] { "LessonAttemptId", "ExerciseId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LessonAttemptAnswers_LessonAttemptId_StepIndex",
                table: "LessonAttemptAnswers",
                columns: new[] { "LessonAttemptId", "StepIndex" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LessonAttempts_LessonId",
                table: "LessonAttempts",
                column: "LessonId");

            migrationBuilder.CreateIndex(
                name: "IX_LessonAttempts_UserId",
                table: "LessonAttempts",
                column: "UserId",
                unique: true,
                filter: "[Status] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_LessonAttempts_UserId_LessonId_Status",
                table: "LessonAttempts",
                columns: new[] { "UserId", "LessonId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_UserLessonCompletions_LessonId",
                table: "UserLessonCompletions",
                column: "LessonId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LessonAttemptAnswers");

            migrationBuilder.DropTable(
                name: "UserLessonCompletions");

            migrationBuilder.DropTable(
                name: "LessonAttempts");

            migrationBuilder.DropColumn(
                name: "PublishedSnapshotJson",
                table: "Lessons");
        }
    }
}
