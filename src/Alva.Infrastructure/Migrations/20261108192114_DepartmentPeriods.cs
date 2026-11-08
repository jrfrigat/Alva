using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Alva.Infrastructure.Migrations
{
    /// <summary>
    /// A sub-team membership becomes a dated period: existing links are open-ended, and a person's
    /// legacy parent adds the membership the old directory implied.
    /// </summary>
    public partial class DepartmentPeriods : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(name: "PK_ActorDepartments", table: "ActorDepartments");
            migrationBuilder.AddColumn<Guid>(name: "Id", table: "ActorDepartments", type: "uuid", nullable: true);
            migrationBuilder.AddColumn<DateOnly>(name: "From", table: "ActorDepartments", type: "date", nullable: false, defaultValue: new DateOnly(1, 1, 1));
            migrationBuilder.AddColumn<DateOnly>(name: "To", table: "ActorDepartments", type: "date", nullable: true);
            migrationBuilder.Sql("""UPDATE "ActorDepartments" SET "Id" = gen_random_uuid() WHERE "Id" IS NULL;""");
            migrationBuilder.Sql("""UPDATE "ActorDepartments" SET "From" = DATE '0001-01-01' WHERE "From" = DATE '-infinity';""");
            migrationBuilder.AlterColumn<Guid>(name: "Id", table: "ActorDepartments", type: "uuid", nullable: false, oldClrType: typeof(Guid), oldType: "uuid", oldNullable: true);
            migrationBuilder.Sql("""
                INSERT INTO "ActorDepartments" ("Id", "ActorId", "DepartmentId", "From", "To")
                SELECT gen_random_uuid(), a."Id", a."ParentId", DATE '0001-01-01', NULL
                FROM "Actors" a
                WHERE a."Kind" = 0 AND a."ParentId" IS NOT NULL
                  AND NOT EXISTS (
                      SELECT 1 FROM "ActorDepartments" d
                      WHERE d."ActorId" = a."Id" AND d."DepartmentId" = a."ParentId");
                """);
            migrationBuilder.AddPrimaryKey(name: "PK_ActorDepartments", table: "ActorDepartments", column: "Id");
            migrationBuilder.CreateIndex(name: "IX_ActorDepartments_ActorId_From_To", table: "ActorDepartments", columns: new[] { "ActorId", "From", "To" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(name: "PK_ActorDepartments", table: "ActorDepartments");
            migrationBuilder.DropIndex(name: "IX_ActorDepartments_ActorId_From_To", table: "ActorDepartments");
            migrationBuilder.Sql("""
                DELETE FROM "ActorDepartments" a
                USING "ActorDepartments" b
                WHERE a."ActorId" = b."ActorId" AND a."DepartmentId" = b."DepartmentId"
                  AND (a."From" > b."From" OR (a."From" = b."From" AND a."Id" > b."Id"));
                """);
            migrationBuilder.DropColumn(name: "Id", table: "ActorDepartments");
            migrationBuilder.DropColumn(name: "From", table: "ActorDepartments");
            migrationBuilder.DropColumn(name: "To", table: "ActorDepartments");
            migrationBuilder.AddPrimaryKey(name: "PK_ActorDepartments", table: "ActorDepartments", columns: new[] { "ActorId", "DepartmentId" });
        }
    }
}
