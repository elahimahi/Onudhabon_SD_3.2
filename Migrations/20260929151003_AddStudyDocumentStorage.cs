using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Onudhabon.Migrations;

public partial class AddStudyDocumentStorage : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "StudyDocuments",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OwnerKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                FileName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                ExpiresAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_StudyDocuments", document => document.Id));

        migrationBuilder.CreateTable(
            name: "StudyDocumentChunks",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                StudyDocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                PageNumber = table.Column<int>(type: "integer", nullable: false),
                ChunkIndex = table.Column<int>(type: "integer", nullable: false),
                Content = table.Column<string>(type: "text", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_StudyDocumentChunks", chunk => chunk.Id);
                table.ForeignKey(
                    name: "FK_StudyDocumentChunks_StudyDocuments_StudyDocumentId",
                    column: chunk => chunk.StudyDocumentId,
                    principalTable: "StudyDocuments",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_StudyDocumentChunks_StudyDocumentId_PageNumber_ChunkIndex",
            table: "StudyDocumentChunks",
            columns: new[] { "StudyDocumentId", "PageNumber", "ChunkIndex" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_StudyDocuments_OwnerKey_ExpiresAt",
            table: "StudyDocuments",
            columns: new[] { "OwnerKey", "ExpiresAt" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "StudyDocumentChunks");
        migrationBuilder.DropTable(name: "StudyDocuments");
    }
}
