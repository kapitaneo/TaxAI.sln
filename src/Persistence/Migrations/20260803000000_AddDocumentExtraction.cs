using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Persistence.Migrations
{
    /// <summary>Adds the append-only document extraction persistence schema.</summary>
    internal sealed partial class AddDocumentExtraction : Migration
    {
        /// <summary>Fixed type identity only; never migration state.</summary>
        public override string ToString() => nameof(AddDocumentExtraction);

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ExtractionAcceptances",
                columns: table => new
                {
                    FirmId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AcceptanceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Scope = table.Column<int>(type: "int", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AcceptedVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ActorKind = table.Column<int>(type: "int", nullable: false),
                    ActorReference = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AcceptedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RetentionUntilUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExtractionAcceptances", x => new { x.FirmId, x.CaseId, x.AcceptanceId });
                    table.ForeignKey(
                        name: "FK_ExtractionAcceptances_IntakeCases_FirmId_CaseId",
                        columns: x => new { x.FirmId, x.CaseId },
                        principalTable: "IntakeCases",
                        principalColumns: new[] { "FirmId", "CaseId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ExtractionAuditEvents",
                columns: table => new
                {
                    FirmId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sequence = table.Column<long>(type: "bigint", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    ActorKind = table.Column<int>(type: "int", nullable: false),
                    ActorReference = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RetentionUntilUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExtractionAuditEvents", x => new { x.FirmId, x.CaseId, x.EventId });
                    table.ForeignKey(
                        name: "FK_ExtractionAuditEvents_IntakeCases_FirmId_CaseId",
                        columns: x => new { x.FirmId, x.CaseId },
                        principalTable: "IntakeCases",
                        principalColumns: new[] { "FirmId", "CaseId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ExtractionClassificationOverrides",
                columns: table => new
                {
                    FirmId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OverrideId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AutomatedClassification = table.Column<int>(type: "int", nullable: false),
                    OverriddenClassification = table.Column<int>(type: "int", nullable: false),
                    ActorKind = table.Column<int>(type: "int", nullable: false),
                    ActorReference = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OverriddenAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RetentionUntilUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExtractionClassificationOverrides", x => new { x.FirmId, x.CaseId, x.OverrideId });
                    table.ForeignKey(
                        name: "FK_ExtractionClassificationOverrides_IntakeCases_FirmId_CaseId",
                        columns: x => new { x.FirmId, x.CaseId },
                        principalTable: "IntakeCases",
                        principalColumns: new[] { "FirmId", "CaseId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ExtractionCorrections",
                columns: table => new
                {
                    FirmId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CorrectionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Field = table.Column<int>(type: "int", nullable: false),
                    BoxCode = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true, collation: "Latin1_General_BIN2"),
                    PriorValue = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    CorrectedValue = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ActorKind = table.Column<int>(type: "int", nullable: false),
                    ActorReference = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SuppliedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    PrecedingVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RetentionUntilUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExtractionCorrections", x => new { x.FirmId, x.CaseId, x.CorrectionId });
                    table.ForeignKey(
                        name: "FK_ExtractionCorrections_IntakeCases_FirmId_CaseId",
                        columns: x => new { x.FirmId, x.CaseId },
                        principalTable: "IntakeCases",
                        principalColumns: new[] { "FirmId", "CaseId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ExtractionDuplicateCandidates",
                columns: table => new
                {
                    FirmId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CandidateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ComparedDocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DetectedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RetentionUntilUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExtractionDuplicateCandidates", x => new { x.FirmId, x.CaseId, x.CandidateId });
                    table.ForeignKey(
                        name: "FK_ExtractionDuplicateCandidates_IntakeCases_FirmId_CaseId",
                        columns: x => new { x.FirmId, x.CaseId },
                        principalTable: "IntakeCases",
                        principalColumns: new[] { "FirmId", "CaseId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ExtractionFieldOutcomes",
                columns: table => new
                {
                    FirmId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    Ordinal = table.Column<int>(type: "int", nullable: false),
                    FieldKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_BIN2"),
                    OutcomeKind = table.Column<int>(type: "int", nullable: false),
                    RetentionUntilUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExtractionFieldOutcomes", x => new { x.FirmId, x.CaseId, x.DocumentId, x.Version, x.Ordinal });
                    table.ForeignKey(
                        name: "FK_ExtractionFieldOutcomes_IntakeCases_FirmId_CaseId",
                        columns: x => new { x.FirmId, x.CaseId },
                        principalTable: "IntakeCases",
                        principalColumns: new[] { "FirmId", "CaseId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ExtractionVersions",
                columns: table => new
                {
                    FirmId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    ExtractionVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ParticipantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AutomatedClassification = table.Column<int>(type: "int", nullable: false),
                    OverriddenClassification = table.Column<int>(type: "int", nullable: true),
                    OverrideActorReference = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OverriddenAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    EmployerDisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    EmployeeDisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ProviderLabel = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true, collation: "Latin1_General_BIN2"),
                    Confidence = table.Column<decimal>(type: "decimal(9,6)", precision: 9, scale: 6, nullable: true),
                    AcceptanceState = table.Column<int>(type: "int", nullable: false),
                    PrecedingVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RetentionUntilUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExtractionVersions", x => new { x.FirmId, x.CaseId, x.DocumentId, x.Version });
                    table.ForeignKey(
                        name: "FK_ExtractionVersions_IntakeCases_FirmId_CaseId",
                        columns: x => new { x.FirmId, x.CaseId },
                        principalTable: "IntakeCases",
                        principalColumns: new[] { "FirmId", "CaseId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MedicalEvidenceVersions",
                columns: table => new
                {
                    FirmId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    MedicalEvidenceVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ParticipantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ServiceDateAsPrinted = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    GrossAmountAsPrinted = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    CurrencyIsKnown = table.Column<bool>(type: "bit", nullable: false),
                    CurrencyCodeAsPrinted = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    ProviderDisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    PatientDisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ReceiptReference = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    ProviderLabel = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true, collation: "Latin1_General_BIN2"),
                    Confidence = table.Column<decimal>(type: "decimal(9,6)", precision: 9, scale: 6, nullable: true),
                    AcceptanceState = table.Column<int>(type: "int", nullable: false),
                    PrecedingVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RetentionUntilUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MedicalEvidenceVersions", x => new { x.FirmId, x.CaseId, x.DocumentId, x.Version });
                    table.ForeignKey(
                        name: "FK_MedicalEvidenceVersions_IntakeCases_FirmId_CaseId",
                        columns: x => new { x.FirmId, x.CaseId },
                        principalTable: "IntakeCases",
                        principalColumns: new[] { "FirmId", "CaseId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ExtractionDuplicateResolutions",
                columns: table => new
                {
                    FirmId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ResolutionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CandidateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Resolution = table.Column<int>(type: "int", nullable: false),
                    ActorKind = table.Column<int>(type: "int", nullable: false),
                    ActorReference = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ResolvedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RetentionUntilUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExtractionDuplicateResolutions", x => new { x.FirmId, x.CaseId, x.ResolutionId });
                    table.ForeignKey(
                        name: "FK_ExtractionDuplicateResolutions_ExtractionDuplicateCandidates_FirmId_CaseId_CandidateId",
                        columns: x => new { x.FirmId, x.CaseId, x.CandidateId },
                        principalTable: "ExtractionDuplicateCandidates",
                        principalColumns: new[] { "FirmId", "CaseId", "CandidateId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ExtractionBoxValues",
                columns: table => new
                {
                    FirmId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    Ordinal = table.Column<int>(type: "int", nullable: false),
                    BoxCode = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false, collation: "Latin1_General_BIN2"),
                    PrintedValue = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    RetentionUntilUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExtractionBoxValues", x => new { x.FirmId, x.CaseId, x.DocumentId, x.Version, x.Ordinal });
                    table.ForeignKey(
                        name: "FK_ExtractionBoxValues_ExtractionVersions_FirmId_CaseId_DocumentId_Version",
                        columns: x => new { x.FirmId, x.CaseId, x.DocumentId, x.Version },
                        principalTable: "ExtractionVersions",
                        principalColumns: new[] { "FirmId", "CaseId", "DocumentId", "Version" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExtractionAcceptances_FirmId_CaseId_Scope_AcceptedAtUtc",
                table: "ExtractionAcceptances",
                columns: new[] { "FirmId", "CaseId", "Scope", "AcceptedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ExtractionAuditEvents_FirmId_CaseId_Sequence",
                table: "ExtractionAuditEvents",
                columns: new[] { "FirmId", "CaseId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExtractionClassificationOverrides_FirmId_CaseId_DocumentId_OverriddenAtUtc",
                table: "ExtractionClassificationOverrides",
                columns: new[] { "FirmId", "CaseId", "DocumentId", "OverriddenAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ExtractionCorrections_FirmId_CaseId_DocumentId_SuppliedAtUtc",
                table: "ExtractionCorrections",
                columns: new[] { "FirmId", "CaseId", "DocumentId", "SuppliedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ExtractionDuplicateCandidates_FirmId_CaseId_DocumentId_ComparedDocumentId",
                table: "ExtractionDuplicateCandidates",
                columns: new[] { "FirmId", "CaseId", "DocumentId", "ComparedDocumentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExtractionDuplicateResolutions_FirmId_CaseId_CandidateId",
                table: "ExtractionDuplicateResolutions",
                columns: new[] { "FirmId", "CaseId", "CandidateId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExtractionVersions_FirmId_ExtractionVersionId",
                table: "ExtractionVersions",
                columns: new[] { "FirmId", "ExtractionVersionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MedicalEvidenceVersions_FirmId_MedicalEvidenceVersionId",
                table: "MedicalEvidenceVersions",
                columns: new[] { "FirmId", "MedicalEvidenceVersionId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExtractionAcceptances");

            migrationBuilder.DropTable(
                name: "ExtractionAuditEvents");

            migrationBuilder.DropTable(
                name: "ExtractionBoxValues");

            migrationBuilder.DropTable(
                name: "ExtractionClassificationOverrides");

            migrationBuilder.DropTable(
                name: "ExtractionCorrections");

            migrationBuilder.DropTable(
                name: "ExtractionDuplicateResolutions");

            migrationBuilder.DropTable(
                name: "ExtractionFieldOutcomes");

            migrationBuilder.DropTable(
                name: "MedicalEvidenceVersions");

            migrationBuilder.DropTable(
                name: "ExtractionVersions");

            migrationBuilder.DropTable(
                name: "ExtractionDuplicateCandidates");
        }
    }
}
