using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Alva.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemainingWork : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "RemainingHours",
                table: "Tasks",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RemainingHours",
                table: "Tasks");
        }
    }
}
