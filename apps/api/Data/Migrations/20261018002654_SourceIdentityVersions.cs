using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RabbitHole.Data.Migrations
{
    /// <inheritdoc />
    public partial class SourceIdentityVersions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ItemSources_ItemId_SourceKey",
                table: "ItemSources");

            migrationBuilder.AddColumn<int>(
                name: "IdentityVersion",
                table: "Sources",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SourceVersion",
                table: "ItemSources",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_ItemSources_ItemId_SourceKey_SourceVersion",
                table: "ItemSources",
                columns: new[] { "ItemId", "SourceKey", "SourceVersion" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new System.NotSupportedException("Restore a pre-upgrade backup to run the older application without losing feed namespaces.");
        }
    }
}
