using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RabbitHole.Data.Migrations
{
    /// <inheritdoc />
    public partial class ArchiveDiscovery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ArchiveCandidate",
                table: "PublicCatalogEntries",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<long>(
                name: "RotationKey",
                table: "PublicCatalogEntries",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ArchiveCandidate",
                table: "PublicCatalogEntries");

            migrationBuilder.DropColumn(
                name: "RotationKey",
                table: "PublicCatalogEntries");
        }
    }
}
