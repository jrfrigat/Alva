using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Alva.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TaskSprints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TaskSprints",
                columns: table => new
                {
                    TaskId = table.Column<Guid>(type: "uuid", nullable: false),
                    SprintId = table.Column<Guid>(type: "uuid", nullable: false),
                    TeamId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskSprints", x => new { x.TaskId, x.SprintId });
                    table.ForeignKey(
                        name: "FK_TaskSprints_Sprints_SprintId",
                        column: x => x.SprintId,
                        principalTable: "Sprints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TaskSprints_Tasks_TaskId",
                        column: x => x.TaskId,
                        principalTable: "Tasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            // Спринт задачи становится следствием дат: задаче со спринтом недостающие даты ставятся по окну спринта,
            // чтобы она не выпала из плана, затем кэш заполняется по правилу SprintWindows, и только потом колонка уходит.
            migrationBuilder.Sql("""
                UPDATE "Tasks" AS t
                SET "StartDate" = COALESCE(t."StartDate", LEAST(s."StartDate", t."EndDate")),
                    "EndDate" = COALESCE(t."EndDate", GREATEST(s."EndDate", t."StartDate"))
                FROM "Sprints" AS s
                WHERE t."SprintId" = s."Id" AND (t."StartDate" IS NULL OR t."EndDate" IS NULL);
                """);
            migrationBuilder.Sql("""
                INSERT INTO "TaskSprints" ("TaskId", "SprintId", "TeamId")
                SELECT t."Id", s."Id", t."TeamId"
                FROM "Tasks" AS t
                JOIN "Sprints" AS s ON s."TeamId" = t."TeamId"
                WHERE NOT t."Archived"
                  AND COALESCE(LEAST(t."StartDate", t."EndDate"), t."StartDate", t."EndDate") <= s."EndDate"
                  AND COALESCE(GREATEST(t."StartDate", t."EndDate"), t."StartDate", t."EndDate") >= s."StartDate";
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_Tasks_Sprints_SprintId",
                table: "Tasks");

            migrationBuilder.DropIndex(
                name: "IX_Tasks_SprintId",
                table: "Tasks");

            migrationBuilder.DropIndex(
                name: "IX_Tasks_TeamId_SprintId",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "SprintId",
                table: "Tasks");

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_TeamId_StartDate_EndDate",
                table: "Tasks",
                columns: new[] { "TeamId", "StartDate", "EndDate" });

            migrationBuilder.CreateIndex(
                name: "IX_TaskSprints_SprintId",
                table: "TaskSprints",
                column: "SprintId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskSprints_TeamId_SprintId",
                table: "TaskSprints",
                columns: new[] { "TeamId", "SprintId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Tasks_TeamId_StartDate_EndDate",
                table: "Tasks");

            migrationBuilder.AddColumn<Guid>(
                name: "SprintId",
                table: "Tasks",
                type: "uuid",
                nullable: true);

            // Обратно: задаче возвращается самый ранний из её спринтов.
            migrationBuilder.Sql("""
                UPDATE "Tasks" AS t
                SET "SprintId" = (SELECT x."SprintId" FROM "TaskSprints" AS x JOIN "Sprints" AS s ON s."Id" = x."SprintId"
                                  WHERE x."TaskId" = t."Id" ORDER BY s."StartDate" LIMIT 1);
                """);

            migrationBuilder.DropTable(
                name: "TaskSprints");

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_SprintId",
                table: "Tasks",
                column: "SprintId");

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_TeamId_SprintId",
                table: "Tasks",
                columns: new[] { "TeamId", "SprintId" });

            migrationBuilder.AddForeignKey(
                name: "FK_Tasks_Sprints_SprintId",
                table: "Tasks",
                column: "SprintId",
                principalTable: "Sprints",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
