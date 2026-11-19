using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Siculingo.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HeartsMissionsSocialAndLeagues : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "SourceAttemptId",
                table: "XpLedger",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AddColumn<bool>(
                name: "IsPractice",
                table: "LessonAttempts",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "Friendships",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserLowId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserHighId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequesterUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AddresseeUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    AcceptedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Friendships", x => x.Id);
                    table.CheckConstraint("CK_Friendships_DistinctUsers", "[UserLowId] <> [UserHighId]");
                    table.CheckConstraint("CK_Friendships_ValidParticipants", "([RequesterUserId] = [UserLowId] AND [AddresseeUserId] = [UserHighId]) OR ([RequesterUserId] = [UserHighId] AND [AddresseeUserId] = [UserLowId])");
                    table.ForeignKey(
                        name: "FK_Friendships_AspNetUsers_AddresseeUserId",
                        column: x => x.AddresseeUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Friendships_AspNetUsers_RequesterUserId",
                        column: x => x.RequesterUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Friendships_AspNetUsers_UserHighId",
                        column: x => x.UserHighId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Friendships_AspNetUsers_UserLowId",
                        column: x => x.UserLowId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UserBlocks",
                columns: table => new
                {
                    BlockerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BlockedUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserBlocks", x => new { x.BlockerUserId, x.BlockedUserId });
                    table.CheckConstraint("CK_UserBlocks_DistinctUsers", "[BlockerUserId] <> [BlockedUserId]");
                    table.ForeignKey(
                        name: "FK_UserBlocks_AspNetUsers_BlockedUserId",
                        column: x => x.BlockedUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UserBlocks_AspNetUsers_BlockerUserId",
                        column: x => x.BlockerUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UserHearts",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CurrentHearts = table.Column<int>(type: "int", nullable: false),
                    RegenerationAnchorUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserHearts", x => x.UserId);
                    table.CheckConstraint("CK_UserHearts_Range", "[CurrentHearts] >= 0 AND [CurrentHearts] <= 5");
                    table.ForeignKey(
                        name: "FK_UserHearts_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserLeagueProfiles",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CurrentTier = table.Column<int>(type: "int", nullable: false),
                    LastSettledWeekStartLocal = table.Column<DateOnly>(type: "date", nullable: true),
                    LastOutcome = table.Column<int>(type: "int", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserLeagueProfiles", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_UserLeagueProfiles_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserMissionProgress",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Scope = table.Column<int>(type: "int", nullable: false),
                    MissionCode = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false, collation: "Latin1_General_100_CI_AS"),
                    PeriodStartLocal = table.Column<DateOnly>(type: "date", nullable: false),
                    CurrentValue = table.Column<int>(type: "int", nullable: false),
                    TargetValue = table.Column<int>(type: "int", nullable: false),
                    RewardXp = table.Column<int>(type: "int", nullable: false),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RewardLedgerEntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserMissionProgress", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserMissionProgress_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserMissionProgress_XpLedger_RewardLedgerEntryId",
                        column: x => x.RewardLedgerEntryId,
                        principalTable: "XpLedger",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UserPrivacySettings",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProfileVisibility = table.Column<int>(type: "int", nullable: false),
                    AllowFriendRequests = table.Column<bool>(type: "bit", nullable: false),
                    ShowInLeaderboard = table.Column<bool>(type: "bit", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserPrivacySettings", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_UserPrivacySettings_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WeeklyLeagueStandings",
                columns: table => new
                {
                    WeekStartLocal = table.Column<DateOnly>(type: "date", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Tier = table.Column<int>(type: "int", nullable: false),
                    FinalWeeklyXp = table.Column<int>(type: "int", nullable: true),
                    FinalRank = table.Column<int>(type: "int", nullable: true),
                    Outcome = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    SettledAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WeeklyLeagueStandings", x => new { x.WeekStartLocal, x.UserId });
                    table.ForeignKey(
                        name: "FK_WeeklyLeagueStandings_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_XpLedger_LocalActivityDate_UserId",
                table: "XpLedger",
                columns: new[] { "LocalActivityDate", "UserId" })
                .Annotation("SqlServer:Include", new[] { "Amount" });

            migrationBuilder.CreateIndex(
                name: "IX_Friendships_AddresseeUserId_Status_CreatedAtUtc",
                table: "Friendships",
                columns: new[] { "AddresseeUserId", "Status", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Friendships_RequesterUserId_Status_CreatedAtUtc",
                table: "Friendships",
                columns: new[] { "RequesterUserId", "Status", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Friendships_UserHighId",
                table: "Friendships",
                column: "UserHighId");

            migrationBuilder.CreateIndex(
                name: "IX_Friendships_UserLowId_UserHighId",
                table: "Friendships",
                columns: new[] { "UserLowId", "UserHighId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserBlocks_BlockedUserId",
                table: "UserBlocks",
                column: "BlockedUserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserMissionProgress_RewardLedgerEntryId",
                table: "UserMissionProgress",
                column: "RewardLedgerEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_UserMissionProgress_UserId_MissionCode_PeriodStartLocal",
                table: "UserMissionProgress",
                columns: new[] { "UserId", "MissionCode", "PeriodStartLocal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserMissionProgress_UserId_Scope_PeriodStartLocal",
                table: "UserMissionProgress",
                columns: new[] { "UserId", "Scope", "PeriodStartLocal" });

            migrationBuilder.CreateIndex(
                name: "IX_WeeklyLeagueStandings_Outcome_WeekStartLocal",
                table: "WeeklyLeagueStandings",
                columns: new[] { "Outcome", "WeekStartLocal" });

            migrationBuilder.CreateIndex(
                name: "IX_WeeklyLeagueStandings_UserId",
                table: "WeeklyLeagueStandings",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_WeeklyLeagueStandings_WeekStartLocal_Tier",
                table: "WeeklyLeagueStandings",
                columns: new[] { "WeekStartLocal", "Tier" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Friendships");

            migrationBuilder.DropTable(
                name: "UserBlocks");

            migrationBuilder.DropTable(
                name: "UserHearts");

            migrationBuilder.DropTable(
                name: "UserLeagueProfiles");

            migrationBuilder.DropTable(
                name: "UserMissionProgress");

            migrationBuilder.DropTable(
                name: "UserPrivacySettings");

            migrationBuilder.DropTable(
                name: "WeeklyLeagueStandings");

            migrationBuilder.DropIndex(
                name: "IX_XpLedger_LocalActivityDate_UserId",
                table: "XpLedger");

            migrationBuilder.DropColumn(
                name: "IsPractice",
                table: "LessonAttempts");

            migrationBuilder.AlterColumn<Guid>(
                name: "SourceAttemptId",
                table: "XpLedger",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);
        }
    }
}
