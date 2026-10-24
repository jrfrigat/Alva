using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Alva.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TaskFixedDates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "DatesFixed",
                table: "Tasks",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DatesFixed",
                table: "Tasks");
        }
    }
}
