using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Alva.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SprintSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ClosedAt",
                table: "Sprints",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CommitmentDoneHours",
                table: "Sprints",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CommitmentHours",
                table: "Sprints",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CommitmentTasks",
                table: "Sprints",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CommittedAt",
                table: "Sprints",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ResultDoneHours",
                table: "Sprints",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ResultHours",
                table: "Sprints",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ResultTasks",
                table: "Sprints",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ClosedAt",
                table: "Sprints");

            migrationBuilder.DropColumn(
                name: "CommitmentDoneHours",
                table: "Sprints");

            migrationBuilder.DropColumn(
                name: "CommitmentHours",
                table: "Sprints");

            migrationBuilder.DropColumn(
                name: "CommitmentTasks",
                table: "Sprints");

            migrationBuilder.DropColumn(
                name: "CommittedAt",
                table: "Sprints");

            migrationBuilder.DropColumn(
                name: "ResultDoneHours",
                table: "Sprints");

            migrationBuilder.DropColumn(
                name: "ResultHours",
                table: "Sprints");

            migrationBuilder.DropColumn(
                name: "ResultTasks",
                table: "Sprints");
        }
    }
}
