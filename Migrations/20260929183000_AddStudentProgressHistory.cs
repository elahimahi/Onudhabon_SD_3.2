using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Onudhabon.Migrations;

public partial class AddStudentProgressHistory : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "StudentProgressChanges",
            columns: table => new
            {
                Id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                StudentId = table.Column<int>(type: "integer", nullable: false),
                ActionType = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                Summary = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                ChangedBy = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                ChangedByRole = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp without time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_StudentProgressChanges", change => change.Id);
                table.ForeignKey("FK_StudentProgressChanges_Students_StudentId", change => change.StudentId,
                    "Students", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex("IX_StudentProgressChanges_StudentId_CreatedAt", "StudentProgressChanges",
            new[] { "StudentId", "CreatedAt" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "StudentProgressChanges");
    }
}
