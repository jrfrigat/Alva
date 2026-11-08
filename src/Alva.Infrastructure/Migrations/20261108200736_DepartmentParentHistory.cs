using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Alva.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DepartmentParentHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DepartmentParentPeriods",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DepartmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ParentId = table.Column<Guid>(type: "uuid", nullable: true),
                    From = table.Column<DateOnly>(type: "date", nullable: false),
                    To = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DepartmentParentPeriods", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DepartmentParentPeriods_Actors_DepartmentId",
                        column: x => x.DepartmentId,
                        principalTable: "Actors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DepartmentParentPeriods_Actors_ParentId",
                        column: x => x.ParentId,
                        principalTable: "Actors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DepartmentParentPeriods_DepartmentId_From_To",
                table: "DepartmentParentPeriods",
                columns: new[] { "DepartmentId", "From", "To" });

            migrationBuilder.CreateIndex(
                name: "IX_DepartmentParentPeriods_ParentId",
                table: "DepartmentParentPeriods",
                column: "ParentId");

            // The current parent becomes the first open period; earlier moves were never recorded.
            migrationBuilder.Sql("""
                INSERT INTO "DepartmentParentPeriods" ("Id", "DepartmentId", "ParentId", "From", "To")
                SELECT gen_random_uuid(), a."Id", a."ParentId", DATE '0001-01-01', NULL
                FROM "Actors" a
                WHERE a."Kind" = 1
                  AND NOT EXISTS (SELECT 1 FROM "DepartmentParentPeriods" p WHERE p."DepartmentId" = a."Id");
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DepartmentParentPeriods");
        }
    }
}
