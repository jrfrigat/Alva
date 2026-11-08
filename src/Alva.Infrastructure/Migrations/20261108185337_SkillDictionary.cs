using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Alva.Infrastructure.Migrations
{
    /// <summary>
    /// Roles become the team's skills: the join table keeps its identifiers, free-text skills and task
    /// requirements are matched to the dictionary by name, and tasks targeted at a role lose their target
    /// so they show up as unassigned instead of pointing at a skill.
    /// </summary>
    public partial class SkillDictionary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameTable(name: "ActorRoles", newName: "ActorSkills");
            migrationBuilder.RenameColumn(name: "RoleId", table: "ActorSkills", newName: "SkillId");
            migrationBuilder.Sql("""ALTER TABLE "ActorSkills" RENAME CONSTRAINT "PK_ActorRoles" TO "PK_ActorSkills";""");
            migrationBuilder.Sql("""ALTER TABLE "ActorSkills" RENAME CONSTRAINT "FK_ActorRoles_Actors_ActorId" TO "FK_ActorSkills_Actors_ActorId";""");
            migrationBuilder.Sql("""ALTER TABLE "ActorSkills" RENAME CONSTRAINT "FK_ActorRoles_Actors_RoleId" TO "FK_ActorSkills_Actors_SkillId";""");
            migrationBuilder.Sql("""ALTER INDEX "IX_ActorRoles_RoleId" RENAME TO "IX_ActorSkills_SkillId";""");

            // The dictionary gains one skill per distinct name used by people or tasks. A role with the
            // same name already exists as an actor and is reused, so its identifier stays unchanged.
            migrationBuilder.Sql("""
                INSERT INTO "Actors" ("Id", "TeamId", "Kind", "Name", "Archived")
                SELECT gen_random_uuid(), src."TeamId", 2, MIN(src."Name"), false
                FROM (
                    SELECT DISTINCT a."TeamId", regexp_replace(btrim(s), '\s+', ' ', 'g') AS "Name"
                    FROM "Actors" a CROSS JOIN LATERAL unnest(a."Skills") s
                    WHERE a."Kind" = 0 AND btrim(s) <> ''
                    UNION
                    SELECT DISTINCT t."TeamId", regexp_replace(btrim(s), '\s+', ' ', 'g')
                    FROM "Tasks" t CROSS JOIN LATERAL unnest(t."RequiredSkills") s
                    WHERE btrim(s) <> ''
                ) src
                WHERE NOT EXISTS (
                    SELECT 1 FROM "Actors" sk
                    WHERE sk."TeamId" = src."TeamId" AND sk."Kind" = 2 AND lower(sk."Name") = lower(src."Name"))
                GROUP BY src."TeamId", lower(src."Name");
                """);

            migrationBuilder.CreateTable(
                name: "TaskSkills",
                columns: table => new
                {
                    TaskId = table.Column<Guid>(type: "uuid", nullable: false),
                    SkillId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskSkills", x => new { x.TaskId, x.SkillId });
                    table.ForeignKey(
                        name: "FK_TaskSkills_Actors_SkillId",
                        column: x => x.SkillId,
                        principalTable: "Actors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TaskSkills_Tasks_TaskId",
                        column: x => x.TaskId,
                        principalTable: "Tasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.Sql("""
                INSERT INTO "ActorSkills" ("ActorId", "SkillId")
                SELECT DISTINCT a."Id", sk."Id"
                FROM "Actors" a CROSS JOIN LATERAL unnest(a."Skills") s
                JOIN "Actors" sk ON sk."TeamId" = a."TeamId" AND sk."Kind" = 2
                    AND lower(sk."Name") = lower(regexp_replace(btrim(s), '\s+', ' ', 'g'))
                WHERE a."Kind" = 0 AND btrim(s) <> ''
                ON CONFLICT DO NOTHING;
                """);

            migrationBuilder.Sql("""
                INSERT INTO "TaskSkills" ("TaskId", "SkillId")
                SELECT DISTINCT t."Id", sk."Id"
                FROM "Tasks" t CROSS JOIN LATERAL unnest(t."RequiredSkills") s
                JOIN "Actors" sk ON sk."TeamId" = t."TeamId" AND sk."Kind" = 2
                    AND lower(sk."Name") = lower(regexp_replace(btrim(s), '\s+', ' ', 'g'))
                WHERE btrim(s) <> ''
                ON CONFLICT DO NOTHING;
                """);

            // A task targeted at a role cannot be assigned to a skill: leave it unassigned and name every
            // affected task in the migration log so the plan can be repaired deliberately.
            migrationBuilder.Sql("""
                DO $$
                DECLARE ids text;
                BEGIN
                    SELECT string_agg(t."Id"::text, ', ' ORDER BY t."Id") INTO ids
                    FROM "Assignments" a JOIN "Tasks" t ON t."Id" = a."TaskId"
                    WHERE a."TargetId" IN (SELECT "Id" FROM "Actors" WHERE "Kind" = 2);
                    RAISE NOTICE 'Skill migration: unassigned role-targeted tasks: %', COALESCE(ids, 'none');
                END $$;
                """);
            migrationBuilder.Sql("""DELETE FROM "Assignments" WHERE "TargetId" IN (SELECT "Id" FROM "Actors" WHERE "Kind" = 2);""");

            migrationBuilder.DropColumn(name: "RequiredSkills", table: "Tasks");
            migrationBuilder.DropColumn(name: "Skills", table: "Actors");

            migrationBuilder.CreateIndex(
                name: "IX_TaskSkills_SkillId",
                table: "TaskSkills",
                column: "SkillId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string[]>(
                name: "RequiredSkills",
                table: "Tasks",
                type: "text[]",
                nullable: false,
                defaultValue: new string[0]);

            migrationBuilder.AddColumn<string[]>(
                name: "Skills",
                table: "Actors",
                type: "text[]",
                nullable: false,
                defaultValue: new string[0]);

            // The names come back from the links; the tasks unassigned in Up cannot be restored here.
            migrationBuilder.Sql("""
                UPDATE "Tasks" t
                SET "RequiredSkills" = COALESCE((
                    SELECT array_agg(sk."Name" ORDER BY sk."Name")
                    FROM "TaskSkills" ts JOIN "Actors" sk ON sk."Id" = ts."SkillId"
                    WHERE ts."TaskId" = t."Id"), '{}');
                """);
            migrationBuilder.Sql("""
                UPDATE "Actors" a
                SET "Skills" = COALESCE((
                    SELECT array_agg(sk."Name" ORDER BY sk."Name")
                    FROM "ActorSkills" asg JOIN "Actors" sk ON sk."Id" = asg."SkillId"
                    WHERE asg."ActorId" = a."Id"), '{}')
                WHERE a."Kind" = 0;
                """);

            migrationBuilder.CreateTable(
                name: "ActorRoles",
                columns: table => new
                {
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoleId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActorRoles", x => new { x.ActorId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_ActorRoles_Actors_ActorId",
                        column: x => x.ActorId,
                        principalTable: "Actors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ActorRoles_Actors_RoleId",
                        column: x => x.RoleId,
                        principalTable: "Actors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.Sql("""INSERT INTO "ActorRoles" ("ActorId", "RoleId") SELECT "ActorId", "SkillId" FROM "ActorSkills";""");
            migrationBuilder.DropTable(name: "TaskSkills");
            migrationBuilder.DropTable(name: "ActorSkills");

            migrationBuilder.CreateIndex(
                name: "IX_ActorRoles_RoleId",
                table: "ActorRoles",
                column: "RoleId");
        }
    }
}
