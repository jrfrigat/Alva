using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Alva.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TaskPriority : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BacklogRank",
                table: "Tasks",
                type: "integer",
                nullable: true);

            // Существующие задачи получают Normal (1), а не Low (0): приоритет им ещё никто не ставил.
            migrationBuilder.AddColumn<int>(
                name: "Priority",
                table: "Tasks",
                type: "integer",
                nullable: false,
                defaultValue: 1);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BacklogRank",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "Priority",
                table: "Tasks");
        }
    }
}
