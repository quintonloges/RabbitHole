using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RabbitHole.Data.Migrations
{
    /// <inheritdoc />
    public partial class PublicCatalogIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PublicCatalogEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IsPublic = table.Column<bool>(type: "boolean", nullable: false),
                    MergedIntoId = table.Column<Guid>(type: "uuid", nullable: true),
                    Title = table.Column<string>(type: "text", nullable: false),
                    CanonicalUrl = table.Column<string>(type: "text", nullable: false),
                    Author = table.Column<string>(type: "text", nullable: true),
                    PublishedAt = table.Column<long>(type: "bigint", nullable: true),
                    PublishedDay = table.Column<int>(type: "integer", nullable: true),
                    IngestedAt = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAt = table.Column<long>(type: "bigint", nullable: false),
                    Type = table.Column<string>(type: "text", nullable: false),
                    Provider = table.Column<string>(type: "text", nullable: false),
                    SourceName = table.Column<string>(type: "text", nullable: false),
                    SourceKey = table.Column<string>(type: "text", nullable: false),
                    TypeKey = table.Column<string>(type: "text", nullable: false),
                    SearchText = table.Column<string>(type: "text", nullable: false),
                    Excerpt = table.Column<string>(type: "text", nullable: true),
                    EmbedUrl = table.Column<string>(type: "text", nullable: true),
                    ImageUrl = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PublicCatalogEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PublicCatalogEntries_Items_Id",
                        column: x => x.Id,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PublicCatalogTopics",
                columns: table => new
                {
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    TopicId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PublicCatalogTopics", x => new { x.ItemId, x.TopicId });
                    table.ForeignKey(
                        name: "FK_PublicCatalogTopics_PublicCatalogEntries_ItemId",
                        column: x => x.ItemId,
                        principalTable: "PublicCatalogEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PublicCatalogEntries_IsPublic_MergedIntoId_IngestedAt_Id",
                table: "PublicCatalogEntries",
                columns: new[] { "IsPublic", "MergedIntoId", "IngestedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_PublicCatalogEntries_PublishedDay",
                table: "PublicCatalogEntries",
                column: "PublishedDay");

            migrationBuilder.CreateIndex(
                name: "IX_PublicCatalogEntries_SourceKey",
                table: "PublicCatalogEntries",
                column: "SourceKey");

            migrationBuilder.CreateIndex(
                name: "IX_PublicCatalogEntries_TypeKey",
                table: "PublicCatalogEntries",
                column: "TypeKey");

            migrationBuilder.CreateIndex(
                name: "IX_PublicCatalogTopics_TopicId",
                table: "PublicCatalogTopics",
                column: "TopicId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PublicCatalogTopics");

            migrationBuilder.DropTable(
                name: "PublicCatalogEntries");
        }
    }
}
