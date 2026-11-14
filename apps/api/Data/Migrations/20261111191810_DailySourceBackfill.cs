using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RabbitHole.Data.Migrations
{
    /// <inheritdoc />
    public partial class DailySourceBackfill : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "BackfillEnabled",
                table: "Sources",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "BackfillFailureCount",
                table: "Sources",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "BackfillImportedCount",
                table: "Sources",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "BackfillLastAttemptAt",
                table: "Sources",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "BackfillLastSuccessAt",
                table: "Sources",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BackfillLimit",
                table: "Sources",
                type: "integer",
                nullable: false,
                defaultValue: 3);

            migrationBuilder.AddColumn<string>(
                name: "BackfillMessage",
                table: "Sources",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "BackfillNextAt",
                table: "Sources",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BackfillStateJson",
                table: "Sources",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BackfillStatus",
                table: "Sources",
                type: "text",
                nullable: false,
                defaultValue: "pending");

            migrationBuilder.AddColumn<string>(
                name: "BackfillUrl",
                table: "Sources",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "Jobs",
                type: "text",
                nullable: false,
                defaultValue: "refresh");

            migrationBuilder.CreateIndex(
                name: "IX_Sources_BackfillNextAt",
                table: "Sources",
                column: "BackfillNextAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Sources_BackfillNextAt",
                table: "Sources");

            migrationBuilder.DropColumn(
                name: "BackfillEnabled",
                table: "Sources");

            migrationBuilder.DropColumn(
                name: "BackfillFailureCount",
                table: "Sources");

            migrationBuilder.DropColumn(
                name: "BackfillImportedCount",
                table: "Sources");

            migrationBuilder.DropColumn(
                name: "BackfillLastAttemptAt",
                table: "Sources");

            migrationBuilder.DropColumn(
                name: "BackfillLastSuccessAt",
                table: "Sources");

            migrationBuilder.DropColumn(
                name: "BackfillLimit",
                table: "Sources");

            migrationBuilder.DropColumn(
                name: "BackfillMessage",
                table: "Sources");

            migrationBuilder.DropColumn(
                name: "BackfillNextAt",
                table: "Sources");

            migrationBuilder.DropColumn(
                name: "BackfillStateJson",
                table: "Sources");

            migrationBuilder.DropColumn(
                name: "BackfillStatus",
                table: "Sources");

            migrationBuilder.DropColumn(
                name: "BackfillUrl",
                table: "Sources");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "Jobs");
        }
    }
}
