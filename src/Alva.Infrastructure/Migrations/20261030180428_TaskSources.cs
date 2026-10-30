using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Alva.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TaskSources : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TaskSources",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TeamId = table.Column<Guid>(type: "uuid", nullable: false),
                    Module = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    SettingsJson = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: false),
                    ProtectedSecrets = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    AutoSyncMinutes = table.Column<int>(type: "integer", nullable: true),
                    SyncedThrough = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastRunAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastOk = table.Column<bool>(type: "boolean", nullable: true),
                    LastMessage = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: true),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskSources", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TaskSources_Teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ExternalTaskLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConnectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    TaskId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExternalKey = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ExternalUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ExternalUpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExternalTaskLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExternalTaskLinks_TaskSources_ConnectionId",
                        column: x => x.ConnectionId,
                        principalTable: "TaskSources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ExternalTaskLinks_Tasks_TaskId",
                        column: x => x.TaskId,
                        principalTable: "Tasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TaskSourceRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConnectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FinishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Ok = table.Column<bool>(type: "boolean", nullable: false),
                    Created = table.Column<int>(type: "integer", nullable: false),
                    Updated = table.Column<int>(type: "integer", nullable: false),
                    Unchanged = table.Column<int>(type: "integer", nullable: false),
                    Skipped = table.Column<int>(type: "integer", nullable: false),
                    Message = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskSourceRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TaskSourceRuns_TaskSources_ConnectionId",
                        column: x => x.ConnectionId,
                        principalTable: "TaskSources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExternalTaskLinks_ConnectionId_ExternalKey",
                table: "ExternalTaskLinks",
                columns: new[] { "ConnectionId", "ExternalKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExternalTaskLinks_TaskId",
                table: "ExternalTaskLinks",
                column: "TaskId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TaskSourceRuns_ConnectionId_StartedAt",
                table: "TaskSourceRuns",
                columns: new[] { "ConnectionId", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TaskSources_TeamId_Name",
                table: "TaskSources",
                columns: new[] { "TeamId", "Name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExternalTaskLinks");

            migrationBuilder.DropTable(
                name: "TaskSourceRuns");

            migrationBuilder.DropTable(
                name: "TaskSources");
        }
    }
}
