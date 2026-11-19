using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Siculingo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EditorialAdministrationAndModeration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ApprovedAtUtc",
                table: "Lessons",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApprovedByName",
                table: "Lessons",
                type: "nvarchar(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ApprovedByUserId",
                table: "Lessons",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<byte>(
                name: "EditorialState",
                table: "Lessons",
                type: "tinyint",
                nullable: false,
                defaultValue: (byte)0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastEditedAtUtc",
                table: "Lessons",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LastEditedByUserId",
                table: "Lessons",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OrthographicVariants",
                table: "Lessons",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PublishedAtUtc",
                table: "Lessons",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReviewerNotes",
                table: "Lessons",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceReference",
                table: "Lessons",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsSuspended",
                table: "AspNetUsers",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SuspendedAtUtc",
                table: "AspNetUsers",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SuspendedByUserId",
                table: "AspNetUsers",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SuspensionReason",
                table: "AspNetUsers",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AdminAuditEntries",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActorUsername = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    ActionType = table.Column<string>(type: "varchar(80)", unicode: false, maxLength: 80, nullable: false),
                    TargetKind = table.Column<string>(type: "varchar(40)", unicode: false, maxLength: 40, nullable: false),
                    TargetIdentifier = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Summary = table.Column<string>(type: "nvarchar(600)", maxLength: 600, nullable: false),
                    DetailsJson = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdminAuditEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AdminAuditEntries_AspNetUsers_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LinguisticLexemes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Headword = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false, collation: "Latin1_General_100_CI_AS"),
                    MeaningIt = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    UsageContext = table.Column<string>(type: "nvarchar(600)", maxLength: 600, nullable: false),
                    Register = table.Column<byte>(type: "tinyint", nullable: false),
                    OrthographicVariants = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    SourceReference = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ReviewState = table.Column<byte>(type: "tinyint", nullable: false),
                    ReviewerNotes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    LastEditedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ReviewedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReviewedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LinguisticLexemes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LinguisticLexemes_AspNetUsers_LastEditedByUserId",
                        column: x => x.LastEditedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LinguisticLexemes_AspNetUsers_ReviewedByUserId",
                        column: x => x.ReviewedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UserReports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReporterUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<byte>(type: "tinyint", nullable: false),
                    TargetLessonId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TargetExerciseId = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    TargetUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReasonCategory = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    Message = table.Column<string>(type: "nvarchar(1500)", maxLength: 1500, nullable: false),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    ResolutionNote = table.Column<string>(type: "nvarchar(1500)", maxLength: 1500, nullable: true),
                    ResolvedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ResolvedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserReports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserReports_AspNetUsers_ReporterUserId",
                        column: x => x.ReporterUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UserReports_AspNetUsers_ResolvedByUserId",
                        column: x => x.ResolvedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UserReports_AspNetUsers_TargetUserId",
                        column: x => x.TargetUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UserReports_Lessons_TargetLessonId",
                        column: x => x.TargetLessonId,
                        principalTable: "Lessons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "AspNetRoles",
                columns: new[] { "Id", "ConcurrencyStamp", "Name", "NormalizedName" },
                values: new object[,]
                {
                    { new Guid("8d200001-0000-4000-8000-000000000001"), "8d200001-role-editor-stamp", "Editor", "EDITOR" },
                    { new Guid("8d200002-0000-4000-8000-000000000002"), "8d200002-role-admin-stamp", "Admin", "ADMIN" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Lessons_ApprovedByUserId",
                table: "Lessons",
                column: "ApprovedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Lessons_LastEditedByUserId",
                table: "Lessons",
                column: "LastEditedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_SuspendedByUserId",
                table: "AspNetUsers",
                column: "SuspendedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AdminAuditEntries_ActorUserId",
                table: "AdminAuditEntries",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AdminAuditEntries_CreatedAtUtc",
                table: "AdminAuditEntries",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_AdminAuditEntries_TargetKind_TargetIdentifier_CreatedAtUtc",
                table: "AdminAuditEntries",
                columns: new[] { "TargetKind", "TargetIdentifier", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_LinguisticLexemes_Headword",
                table: "LinguisticLexemes",
                column: "Headword");

            migrationBuilder.CreateIndex(
                name: "IX_LinguisticLexemes_LastEditedByUserId",
                table: "LinguisticLexemes",
                column: "LastEditedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_LinguisticLexemes_ReviewedByUserId",
                table: "LinguisticLexemes",
                column: "ReviewedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserReports_ReporterUserId_CreatedAtUtc",
                table: "UserReports",
                columns: new[] { "ReporterUserId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_UserReports_ResolvedByUserId",
                table: "UserReports",
                column: "ResolvedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserReports_Status_CreatedAtUtc",
                table: "UserReports",
                columns: new[] { "Status", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_UserReports_TargetLessonId",
                table: "UserReports",
                column: "TargetLessonId");

            migrationBuilder.CreateIndex(
                name: "IX_UserReports_TargetUserId",
                table: "UserReports",
                column: "TargetUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_AspNetUsers_AspNetUsers_SuspendedByUserId",
                table: "AspNetUsers",
                column: "SuspendedByUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Lessons_AspNetUsers_ApprovedByUserId",
                table: "Lessons",
                column: "ApprovedByUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Lessons_AspNetUsers_LastEditedByUserId",
                table: "Lessons",
                column: "LastEditedByUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AspNetUsers_AspNetUsers_SuspendedByUserId",
                table: "AspNetUsers");

            migrationBuilder.DropForeignKey(
                name: "FK_Lessons_AspNetUsers_ApprovedByUserId",
                table: "Lessons");

            migrationBuilder.DropForeignKey(
                name: "FK_Lessons_AspNetUsers_LastEditedByUserId",
                table: "Lessons");

            migrationBuilder.DropTable(
                name: "AdminAuditEntries");

            migrationBuilder.DropTable(
                name: "LinguisticLexemes");

            migrationBuilder.DropTable(
                name: "UserReports");

            migrationBuilder.DropIndex(
                name: "IX_Lessons_ApprovedByUserId",
                table: "Lessons");

            migrationBuilder.DropIndex(
                name: "IX_Lessons_LastEditedByUserId",
                table: "Lessons");

            migrationBuilder.DropIndex(
                name: "IX_AspNetUsers_SuspendedByUserId",
                table: "AspNetUsers");

            migrationBuilder.DeleteData(
                table: "AspNetRoles",
                keyColumn: "Id",
                keyValue: new Guid("8d200001-0000-4000-8000-000000000001"));

            migrationBuilder.DeleteData(
                table: "AspNetRoles",
                keyColumn: "Id",
                keyValue: new Guid("8d200002-0000-4000-8000-000000000002"));

            migrationBuilder.DropColumn(
                name: "ApprovedAtUtc",
                table: "Lessons");

            migrationBuilder.DropColumn(
                name: "ApprovedByName",
                table: "Lessons");

            migrationBuilder.DropColumn(
                name: "ApprovedByUserId",
                table: "Lessons");

            migrationBuilder.DropColumn(
                name: "EditorialState",
                table: "Lessons");

            migrationBuilder.DropColumn(
                name: "LastEditedAtUtc",
                table: "Lessons");

            migrationBuilder.DropColumn(
                name: "LastEditedByUserId",
                table: "Lessons");

            migrationBuilder.DropColumn(
                name: "OrthographicVariants",
                table: "Lessons");

            migrationBuilder.DropColumn(
                name: "PublishedAtUtc",
                table: "Lessons");

            migrationBuilder.DropColumn(
                name: "ReviewerNotes",
                table: "Lessons");

            migrationBuilder.DropColumn(
                name: "SourceReference",
                table: "Lessons");

            migrationBuilder.DropColumn(
                name: "IsSuspended",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "SuspendedAtUtc",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "SuspendedByUserId",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "SuspensionReason",
                table: "AspNetUsers");
        }
    }
}
