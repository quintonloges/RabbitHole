using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RabbitHole.Data.Migrations
{
    /// <inheritdoc />
    public partial class RecommendationEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Origin",
                table: "Nodes",
                type: "text",
                nullable: false,
                defaultValue: "legacy-unknown");

            migrationBuilder.AddColumn<string>(
                name: "ConceptTags",
                table: "Items",
                type: "text",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "ExcludedConcepts",
                table: "Items",
                type: "text",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "Origin",
                table: "Holes",
                type: "text",
                nullable: false,
                defaultValue: "legacy-unknown");

            migrationBuilder.AddColumn<string>(
                name: "OriginNote",
                table: "Holes",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RationaleJson",
                table: "Entries",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Recommendation provenance and evidence must be preserved. Restore a matching pre-upgrade database/uploads backup to run the older application.");
        }
    }
}
