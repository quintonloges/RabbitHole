using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RabbitHole.Data.Migrations
{
    /// <inheritdoc />
    public partial class CatalogIdentityAndTopicProvenance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Items_ArxivId",
                table: "Items");

            migrationBuilder.DropIndex(
                name: "IX_Items_CanonicalUrl",
                table: "Items");

            migrationBuilder.DropIndex(
                name: "IX_Items_Doi",
                table: "Items");

            migrationBuilder.DropIndex(
                name: "IX_Items_Provider_ProviderId",
                table: "Items");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ResearchCheckpointAt",
                table: "Sources",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResearchCursor",
                table: "Sources",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ResearchFromDate",
                table: "Sources",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ResearchWindowStartedAt",
                table: "Sources",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "MergedIntoId",
                table: "Items",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ItemIdentities",
                columns: table => new
                {
                    Key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    Scope = table.Column<string>(type: "text", nullable: false),
                    Value = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemIdentities", x => x.Key);
                    table.ForeignKey(
                        name: "FK_ItemIdentities_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ItemSources",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceKey = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    Url = table.Column<string>(type: "text", nullable: false),
                    TopicIds = table.Column<string>(type: "text", nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemSources", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ItemSources_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ItemSources_Sources_SourceId",
                        column: x => x.SourceId,
                        principalTable: "Sources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "ItemTopics",
                columns: table => new
                {
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    TopicId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceAssigned = table.Column<bool>(type: "boolean", nullable: false),
                    KeywordMatched = table.Column<bool>(type: "boolean", nullable: false),
                    LegacyAssigned = table.Column<bool>(type: "boolean", nullable: false),
                    UserOverride = table.Column<bool>(type: "boolean", nullable: true),
                    UserUpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemTopics", x => new { x.ItemId, x.TopicId });
                    table.ForeignKey(
                        name: "FK_ItemTopics_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ItemTopics_Topics_TopicId",
                        column: x => x.TopicId,
                        principalTable: "Topics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Items_ArxivId",
                table: "Items",
                column: "ArxivId");

            migrationBuilder.CreateIndex(
                name: "IX_Items_CanonicalUrl",
                table: "Items",
                column: "CanonicalUrl");

            migrationBuilder.CreateIndex(
                name: "IX_Items_Doi",
                table: "Items",
                column: "Doi");

            migrationBuilder.CreateIndex(
                name: "IX_Items_MergedIntoId",
                table: "Items",
                column: "MergedIntoId");

            migrationBuilder.CreateIndex(
                name: "IX_Items_Provider_ProviderId",
                table: "Items",
                columns: new[] { "Provider", "ProviderId" });

            migrationBuilder.CreateIndex(
                name: "IX_ItemIdentities_ItemId",
                table: "ItemIdentities",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_ItemSources_ItemId_SourceKey",
                table: "ItemSources",
                columns: new[] { "ItemId", "SourceKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ItemSources_SourceId",
                table: "ItemSources",
                column: "SourceId");

            migrationBuilder.CreateIndex(
                name: "IX_ItemTopics_TopicId",
                table: "ItemTopics",
                column: "TopicId");

            migrationBuilder.AddForeignKey(
                name: "FK_Items_Items_MergedIntoId",
                table: "Items",
                column: "MergedIntoId",
                principalTable: "Items",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("This migration retains merged records and new provenance. Restore a pre-upgrade database/uploads backup to run the older application.");
        }
    }
}
