using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Persistence.Migrations
{
    /// <summary>Adds the server-owned client intake persistence schema.</summary>
    internal sealed partial class AddClientIntake : Migration
    {
        /// <summary>Fixed type identity only; never migration state.</summary>
        public override string ToString() => nameof(AddClientIntake);

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "IntakeCases",
                columns: table => new
                {
                    FirmId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PrimaryParticipantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PrimaryTaxpayerDisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    TaxYear = table.Column<int>(type: "int", nullable: false),
                    CaseStatus = table.Column<int>(type: "int", nullable: false),
                    QuestionnaireVersion = table.Column<int>(type: "int", nullable: false),
                    ConcurrencyVersion = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntakeCases", x => new { x.FirmId, x.CaseId });
                    table.CheckConstraint("CK_IntakeCases_TaxYear", "[TaxYear] = 2025");
                    table.ForeignKey(
                        name: "FK_IntakeCases_Firms_FirmId",
                        column: x => x.FirmId,
                        principalTable: "Firms",
                        principalColumn: "FirmId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "IntakeAnswerVersions",
                columns: table => new
                {
                    FirmId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Category = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    Answer = table.Column<int>(type: "int", nullable: false),
                    ActorKind = table.Column<int>(type: "int", nullable: false),
                    ActorReference = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntakeAnswerVersions", x => new { x.FirmId, x.CaseId, x.Category, x.Version });
                    table.CheckConstraint("CK_IntakeAnswerVersions_Answer", "[Answer] IN (0, 1, 2)");
                    table.CheckConstraint("CK_IntakeAnswerVersions_Category", "[Category] IN (0, 1)");
                    table.ForeignKey(
                        name: "FK_IntakeAnswerVersions_IntakeCases_FirmId_CaseId",
                        columns: x => new { x.FirmId, x.CaseId },
                        principalTable: "IntakeCases",
                        principalColumns: new[] { "FirmId", "CaseId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "IntakeAuditEvents",
                columns: table => new
                {
                    FirmId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sequence = table.Column<long>(type: "bigint", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    ActorKind = table.Column<int>(type: "int", nullable: false),
                    ActorReference = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExpectedItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntakeAuditEvents", x => new { x.FirmId, x.CaseId, x.EventId });
                    table.CheckConstraint("CK_IntakeAuditEvents_ActorKind", "[ActorKind] IN (0, 1)");
                    table.CheckConstraint("CK_IntakeAuditEvents_Kind", "[Kind] BETWEEN 0 AND 12");
                    table.ForeignKey(
                        name: "FK_IntakeAuditEvents_IntakeCases_FirmId_CaseId",
                        columns: x => new { x.FirmId, x.CaseId },
                        principalTable: "IntakeCases",
                        principalColumns: new[] { "FirmId", "CaseId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "IntakeExpectedItems",
                columns: table => new
                {
                    FirmId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExpectedItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Category = table.Column<int>(type: "int", nullable: false),
                    Ordinal = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntakeExpectedItems", x => new { x.FirmId, x.CaseId, x.ExpectedItemId });
                    table.CheckConstraint("CK_IntakeExpectedItems_Category", "[Category] IN (0, 1)");
                    table.ForeignKey(
                        name: "FK_IntakeExpectedItems_IntakeCases_FirmId_CaseId",
                        columns: x => new { x.FirmId, x.CaseId },
                        principalTable: "IntakeCases",
                        principalColumns: new[] { "FirmId", "CaseId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "IntakeLinks",
                columns: table => new
                {
                    FirmId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LinkId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CredentialDigest = table.Column<byte[]>(type: "binary(32)", maxLength: 32, nullable: false),
                    IssuedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    SpentAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RevokedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntakeLinks", x => new { x.FirmId, x.CaseId, x.LinkId });
                    table.ForeignKey(
                        name: "FK_IntakeLinks_IntakeCases_FirmId_CaseId",
                        columns: x => new { x.FirmId, x.CaseId },
                        principalTable: "IntakeCases",
                        principalColumns: new[] { "FirmId", "CaseId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "IntakeReviewAssociations",
                columns: table => new
                {
                    FirmId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecordId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssociatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntakeReviewAssociations", x => new { x.FirmId, x.CaseId });
                    table.ForeignKey(
                        name: "FK_IntakeReviewAssociations_IntakeCases_FirmId_CaseId",
                        columns: x => new { x.FirmId, x.CaseId },
                        principalTable: "IntakeCases",
                        principalColumns: new[] { "FirmId", "CaseId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_IntakeReviewAssociations_PersistedReviewRecords_FirmId_RecordId",
                        columns: x => new { x.FirmId, x.RecordId },
                        principalTable: "PersistedReviewRecords",
                        principalColumns: new[] { "FirmId", "RecordId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "IntakeSubmissions",
                columns: table => new
                {
                    FirmId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    ActorKind = table.Column<int>(type: "int", nullable: false),
                    ActorReference = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubmittedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntakeSubmissions", x => new { x.FirmId, x.CaseId, x.Version });
                    table.ForeignKey(
                        name: "FK_IntakeSubmissions_IntakeCases_FirmId_CaseId",
                        columns: x => new { x.FirmId, x.CaseId },
                        principalTable: "IntakeCases",
                        principalColumns: new[] { "FirmId", "CaseId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "IntakeDocuments",
                columns: table => new
                {
                    FirmId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExpectedItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Category = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    ContentKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, collation: "Latin1_General_BIN2"),
                    MediaKind = table.Column<int>(type: "int", nullable: false),
                    ByteCount = table.Column<long>(type: "bigint", nullable: false),
                    Fingerprint = table.Column<byte[]>(type: "binary(32)", maxLength: 32, nullable: false),
                    UploadedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntakeDocuments", x => new { x.FirmId, x.CaseId, x.DocumentId });
                    table.CheckConstraint("CK_IntakeDocuments_ByteCount", "[ByteCount] > 0");
                    table.CheckConstraint("CK_IntakeDocuments_Category", "[Category] IN (0, 1)");
                    table.CheckConstraint("CK_IntakeDocuments_MediaKind", "[MediaKind] IN (0, 1, 2)");
                    table.ForeignKey(
                        name: "FK_IntakeDocuments_IntakeExpectedItems_FirmId_CaseId_ExpectedItemId",
                        columns: x => new { x.FirmId, x.CaseId, x.ExpectedItemId },
                        principalTable: "IntakeExpectedItems",
                        principalColumns: new[] { "FirmId", "CaseId", "ExpectedItemId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "IntakeUploadAttempts",
                columns: table => new
                {
                    FirmId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AttemptId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExpectedItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Outcome = table.Column<int>(type: "int", nullable: false),
                    AttemptedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntakeUploadAttempts", x => new { x.FirmId, x.CaseId, x.AttemptId });
                    table.CheckConstraint("CK_IntakeUploadAttempts_Outcome", "[Outcome] IN (0, 1, 2)");
                    table.ForeignKey(
                        name: "FK_IntakeUploadAttempts_IntakeExpectedItems_FirmId_CaseId_ExpectedItemId",
                        columns: x => new { x.FirmId, x.CaseId, x.ExpectedItemId },
                        principalTable: "IntakeExpectedItems",
                        principalColumns: new[] { "FirmId", "CaseId", "ExpectedItemId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "IntakeClientSessions",
                columns: table => new
                {
                    FirmId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LinkId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SessionDigest = table.Column<byte[]>(type: "binary(32)", maxLength: 32, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    AbsoluteExpiresAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IdleExpiresAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RevokedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntakeClientSessions", x => new { x.FirmId, x.CaseId, x.SessionId });
                    table.ForeignKey(
                        name: "FK_IntakeClientSessions_IntakeLinks_FirmId_CaseId_LinkId",
                        columns: x => new { x.FirmId, x.CaseId, x.LinkId },
                        principalTable: "IntakeLinks",
                        principalColumns: new[] { "FirmId", "CaseId", "LinkId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "IntakeSubmissionAnswers",
                columns: table => new
                {
                    FirmId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubmissionVersion = table.Column<int>(type: "int", nullable: false),
                    Category = table.Column<int>(type: "int", nullable: false),
                    AnswerVersion = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntakeSubmissionAnswers", x => new { x.FirmId, x.CaseId, x.SubmissionVersion, x.Category });
                    table.ForeignKey(
                        name: "FK_IntakeSubmissionAnswers_IntakeAnswerVersions_FirmId_CaseId_Category_AnswerVersion",
                        columns: x => new { x.FirmId, x.CaseId, x.Category, x.AnswerVersion },
                        principalTable: "IntakeAnswerVersions",
                        principalColumns: new[] { "FirmId", "CaseId", "Category", "Version" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_IntakeSubmissionAnswers_IntakeSubmissions_FirmId_CaseId_SubmissionVersion",
                        columns: x => new { x.FirmId, x.CaseId, x.SubmissionVersion },
                        principalTable: "IntakeSubmissions",
                        principalColumns: new[] { "FirmId", "CaseId", "Version" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "IntakeSubmissionDocuments",
                columns: table => new
                {
                    FirmId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubmissionVersion = table.Column<int>(type: "int", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntakeSubmissionDocuments", x => new { x.FirmId, x.CaseId, x.SubmissionVersion, x.DocumentId });
                    table.ForeignKey(
                        name: "FK_IntakeSubmissionDocuments_IntakeDocuments_FirmId_CaseId_DocumentId",
                        columns: x => new { x.FirmId, x.CaseId, x.DocumentId },
                        principalTable: "IntakeDocuments",
                        principalColumns: new[] { "FirmId", "CaseId", "DocumentId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_IntakeSubmissionDocuments_IntakeSubmissions_FirmId_CaseId_SubmissionVersion",
                        columns: x => new { x.FirmId, x.CaseId, x.SubmissionVersion },
                        principalTable: "IntakeSubmissions",
                        principalColumns: new[] { "FirmId", "CaseId", "Version" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IntakeAuditEvents_FirmId_CaseId_Sequence",
                table: "IntakeAuditEvents",
                columns: new[] { "FirmId", "CaseId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IntakeClientSessions_FirmId_CaseId_LinkId",
                table: "IntakeClientSessions",
                columns: new[] { "FirmId", "CaseId", "LinkId" });

            migrationBuilder.CreateIndex(
                name: "IX_IntakeClientSessions_SessionDigest",
                table: "IntakeClientSessions",
                column: "SessionDigest",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IntakeDocuments_FirmId_CaseId_ExpectedItemId_Version",
                table: "IntakeDocuments",
                columns: new[] { "FirmId", "CaseId", "ExpectedItemId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IntakeDocuments_FirmId_CaseId_Fingerprint",
                table: "IntakeDocuments",
                columns: new[] { "FirmId", "CaseId", "Fingerprint" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IntakeExpectedItems_FirmId_CaseId_Category_Ordinal",
                table: "IntakeExpectedItems",
                columns: new[] { "FirmId", "CaseId", "Category", "Ordinal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IntakeLinks_CredentialDigest",
                table: "IntakeLinks",
                column: "CredentialDigest",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IntakeLinks_FirmId_CaseId",
                table: "IntakeLinks",
                columns: new[] { "FirmId", "CaseId" },
                unique: true,
                filter: "[RevokedAtUtc] IS NULL AND [SpentAtUtc] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_IntakeReviewAssociations_FirmId_RecordId",
                table: "IntakeReviewAssociations",
                columns: new[] { "FirmId", "RecordId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IntakeSubmissionAnswers_FirmId_CaseId_Category_AnswerVersion",
                table: "IntakeSubmissionAnswers",
                columns: new[] { "FirmId", "CaseId", "Category", "AnswerVersion" });

            migrationBuilder.CreateIndex(
                name: "IX_IntakeSubmissionDocuments_FirmId_CaseId_DocumentId",
                table: "IntakeSubmissionDocuments",
                columns: new[] { "FirmId", "CaseId", "DocumentId" });

            migrationBuilder.CreateIndex(
                name: "IX_IntakeUploadAttempts_FirmId_CaseId_ExpectedItemId",
                table: "IntakeUploadAttempts",
                columns: new[] { "FirmId", "CaseId", "ExpectedItemId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IntakeAuditEvents");

            migrationBuilder.DropTable(
                name: "IntakeClientSessions");

            migrationBuilder.DropTable(
                name: "IntakeReviewAssociations");

            migrationBuilder.DropTable(
                name: "IntakeSubmissionAnswers");

            migrationBuilder.DropTable(
                name: "IntakeSubmissionDocuments");

            migrationBuilder.DropTable(
                name: "IntakeUploadAttempts");

            migrationBuilder.DropTable(
                name: "IntakeLinks");

            migrationBuilder.DropTable(
                name: "IntakeAnswerVersions");

            migrationBuilder.DropTable(
                name: "IntakeDocuments");

            migrationBuilder.DropTable(
                name: "IntakeSubmissions");

            migrationBuilder.DropTable(
                name: "IntakeExpectedItems");

            migrationBuilder.DropTable(
                name: "IntakeCases");
        }
    }
}
