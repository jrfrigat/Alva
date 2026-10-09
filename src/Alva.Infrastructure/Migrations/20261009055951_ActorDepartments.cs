using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Alva.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ActorDepartments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ActorDepartments",
                columns: table => new
                {
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    DepartmentId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActorDepartments", x => new { x.ActorId, x.DepartmentId });
                    table.ForeignKey(
                        name: "FK_ActorDepartments_Actors_ActorId",
                        column: x => x.ActorId,
                        principalTable: "Actors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ActorDepartments_Actors_DepartmentId",
                        column: x => x.DepartmentId,
                        principalTable: "Actors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ActorDepartments_DepartmentId",
                table: "ActorDepartments",
                column: "DepartmentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ActorDepartments");
        }
    }
}
