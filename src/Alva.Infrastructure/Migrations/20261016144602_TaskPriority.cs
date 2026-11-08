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

            // Existing tasks get Normal (1), not Low (0): nobody has set a priority for them yet.
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
