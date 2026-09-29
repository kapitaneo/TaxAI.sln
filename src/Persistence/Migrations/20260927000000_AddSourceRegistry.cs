using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Persistence.Migrations
{
    /// <summary>Adds the spec 016 T04 source registry table, and nothing else.</summary>
    internal sealed partial class AddSourceRegistry : Migration
    {
        /// <summary>Fixed type identity only; never migration state.</summary>
        public override string ToString() => nameof(AddSourceRegistry);

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SourceRegistrations",
                columns: table => new
                {
                    FirmId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ResourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContentKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_BIN2"),
                    State = table.Column<int>(type: "int", nullable: false),
                    StagedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    BoundAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    PurgeRecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SourceRegistrations", x => new { x.FirmId, x.CaseId, x.ResourceId });
                    table.CheckConstraint("CK_SourceRegistrations_Binding", "([DocumentId] IS NULL AND [BoundAtUtc] IS NULL) OR ([DocumentId] IS NOT NULL AND [BoundAtUtc] IS NOT NULL)");
                    table.CheckConstraint("CK_SourceRegistrations_BoundState", "[State] <> 2 OR [DocumentId] IS NOT NULL");
                    table.CheckConstraint("CK_SourceRegistrations_PurgeState", "([State] IN (3, 4) AND [PurgeRecordedAtUtc] IS NOT NULL) OR ([State] IN (1, 2) AND [PurgeRecordedAtUtc] IS NULL)");
                    table.CheckConstraint("CK_SourceRegistrations_State", "[State] IN (1, 2, 3, 4)");
                    table.ForeignKey(
                        name: "FK_SourceRegistrations_IntakeCases_FirmId_CaseId",
                        columns: x => new { x.FirmId, x.CaseId },
                        principalTable: "IntakeCases",
                        principalColumns: new[] { "FirmId", "CaseId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SourceRegistrations_IntakeDocuments_FirmId_CaseId_DocumentId",
                        columns: x => new { x.FirmId, x.CaseId, x.DocumentId },
                        principalTable: "IntakeDocuments",
                        principalColumns: new[] { "FirmId", "CaseId", "DocumentId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SourceRegistrations_FirmId_CaseId_ContentKey",
                table: "SourceRegistrations",
                columns: new[] { "FirmId", "CaseId", "ContentKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SourceRegistrations_FirmId_CaseId_DocumentId",
                table: "SourceRegistrations",
                columns: new[] { "FirmId", "CaseId", "DocumentId" });

            migrationBuilder.CreateIndex(
                name: "IX_SourceRegistrations_FirmId_State_StagedAtUtc",
                table: "SourceRegistrations",
                columns: new[] { "FirmId", "State", "StagedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SourceRegistrations");
        }
    }
}
