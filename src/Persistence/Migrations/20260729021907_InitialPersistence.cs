using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Persistence.Migrations
{
    /// <inheritdoc />
    internal sealed partial class InitialPersistence : Migration
    {
        /// <summary>Fixed type identity only; never migration state.</summary>
        public override string ToString() => nameof(InitialPersistence);

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Firms",
                columns: table => new
                {
                    FirmId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Firms", x => x.FirmId);
                });

            migrationBuilder.CreateTable(
                name: "PersistedReviewRecords",
                columns: table => new
                {
                    RecordId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FirmId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CaseId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_BIN2"),
                    ParticipantId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_BIN2"),
                    TaxYear = table.Column<int>(type: "int", nullable: false),
                    PackageVersion = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RetentionUntilUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false, comment: "Exclusive UTC retention boundary: the record is purge-eligible at or after this instant.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PersistedReviewRecords", x => new { x.FirmId, x.RecordId });
                });

            migrationBuilder.CreateTable(
                name: "PortalUsers",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Issuer = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false, collation: "Latin1_General_BIN2"),
                    Subject = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false, collation: "Latin1_General_BIN2"),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PortalUsers", x => x.UserId);
                });

            migrationBuilder.CreateTable(
                name: "PersistedSlips",
                columns: table => new
                {
                    SlipId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FirmId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecordId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Ordinal = table.Column<int>(type: "int", nullable: false),
                    SlipTypeCode = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false, collation: "Latin1_General_BIN2"),
                    DocumentReference = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TaxpayerDisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    EmployerDisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PersistedSlips", x => new { x.FirmId, x.SlipId });
                    table.ForeignKey(
                        name: "FK_PersistedSlips_PersistedReviewRecords_FirmId_RecordId",
                        columns: x => new { x.FirmId, x.RecordId },
                        principalTable: "PersistedReviewRecords",
                        principalColumns: new[] { "FirmId", "RecordId" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AuthenticationSessions",
                columns: table => new
                {
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ActiveFirmId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastSeenAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IdleExpiresAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    AbsoluteExpiresAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RevokedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuthenticationSessions", x => x.SessionId);
                    table.ForeignKey(
                        name: "FK_AuthenticationSessions_Firms_ActiveFirmId",
                        column: x => x.ActiveFirmId,
                        principalTable: "Firms",
                        principalColumn: "FirmId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AuthenticationSessions_PortalUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "PortalUsers",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FirmMemberships",
                columns: table => new
                {
                    MembershipId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FirmId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FirmMemberships", x => x.MembershipId);
                    table.ForeignKey(
                        name: "FK_FirmMemberships_Firms_FirmId",
                        column: x => x.FirmId,
                        principalTable: "Firms",
                        principalColumn: "FirmId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FirmMemberships_PortalUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "PortalUsers",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PersistedBoxAmounts",
                columns: table => new
                {
                    BoxAmountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FirmId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SlipId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Ordinal = table.Column<int>(type: "int", nullable: false),
                    BoxCode = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false, collation: "Latin1_General_BIN2"),
                    Amount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PersistedBoxAmounts", x => new { x.FirmId, x.BoxAmountId });
                    table.ForeignKey(
                        name: "FK_PersistedBoxAmounts_PersistedSlips_FirmId_SlipId",
                        columns: x => new { x.FirmId, x.SlipId },
                        principalTable: "PersistedSlips",
                        principalColumns: new[] { "FirmId", "SlipId" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PersistedLineEntries",
                columns: table => new
                {
                    LineEntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FirmId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SlipId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Ordinal = table.Column<int>(type: "int", nullable: false),
                    LineCode = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false, collation: "Latin1_General_BIN2"),
                    Amount = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PersistedLineEntries", x => new { x.FirmId, x.LineEntryId });
                    table.ForeignKey(
                        name: "FK_PersistedLineEntries_PersistedSlips_FirmId_SlipId",
                        columns: x => new { x.FirmId, x.SlipId },
                        principalTable: "PersistedSlips",
                        principalColumns: new[] { "FirmId", "SlipId" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PersistedUnmappedBoxCodes",
                columns: table => new
                {
                    UnmappedBoxCodeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FirmId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SlipId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Ordinal = table.Column<int>(type: "int", nullable: false),
                    BoxCode = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false, collation: "Latin1_General_BIN2")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PersistedUnmappedBoxCodes", x => new { x.FirmId, x.UnmappedBoxCodeId });
                    table.ForeignKey(
                        name: "FK_PersistedUnmappedBoxCodes_PersistedSlips_FirmId_SlipId",
                        columns: x => new { x.FirmId, x.SlipId },
                        principalTable: "PersistedSlips",
                        principalColumns: new[] { "FirmId", "SlipId" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PersistedSourceBoxCodes",
                columns: table => new
                {
                    SourceBoxCodeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FirmId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LineEntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Ordinal = table.Column<int>(type: "int", nullable: false),
                    BoxCode = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false, collation: "Latin1_General_BIN2")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PersistedSourceBoxCodes", x => new { x.FirmId, x.SourceBoxCodeId });
                    table.ForeignKey(
                        name: "FK_PersistedSourceBoxCodes_PersistedLineEntries_FirmId_LineEntryId",
                        columns: x => new { x.FirmId, x.LineEntryId },
                        principalTable: "PersistedLineEntries",
                        principalColumns: new[] { "FirmId", "LineEntryId" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuthenticationSessions_ActiveFirmId",
                table: "AuthenticationSessions",
                column: "ActiveFirmId");

            migrationBuilder.CreateIndex(
                name: "IX_AuthenticationSessions_UserId_RevokedAtUtc",
                table: "AuthenticationSessions",
                columns: new[] { "UserId", "RevokedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_FirmMemberships_FirmId_UserId",
                table: "FirmMemberships",
                columns: new[] { "FirmId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FirmMemberships_UserId_IsActive_FirmId",
                table: "FirmMemberships",
                columns: new[] { "UserId", "IsActive", "FirmId" });

            migrationBuilder.CreateIndex(
                name: "IX_PersistedBoxAmounts_FirmId_SlipId_Ordinal",
                table: "PersistedBoxAmounts",
                columns: new[] { "FirmId", "SlipId", "Ordinal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PersistedLineEntries_FirmId_SlipId_Ordinal",
                table: "PersistedLineEntries",
                columns: new[] { "FirmId", "SlipId", "Ordinal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PersistedReviewRecords_FirmId_CaseId_ParticipantId_TaxYear_PackageVersion",
                table: "PersistedReviewRecords",
                columns: new[] { "FirmId", "CaseId", "ParticipantId", "TaxYear", "PackageVersion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PersistedReviewRecords_FirmId_RetentionUntilUtc",
                table: "PersistedReviewRecords",
                columns: new[] { "FirmId", "RetentionUntilUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PersistedSlips_FirmId_RecordId_Ordinal",
                table: "PersistedSlips",
                columns: new[] { "FirmId", "RecordId", "Ordinal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PersistedSourceBoxCodes_FirmId_LineEntryId_Ordinal",
                table: "PersistedSourceBoxCodes",
                columns: new[] { "FirmId", "LineEntryId", "Ordinal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PersistedUnmappedBoxCodes_FirmId_SlipId_Ordinal",
                table: "PersistedUnmappedBoxCodes",
                columns: new[] { "FirmId", "SlipId", "Ordinal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PortalUsers_Issuer_Subject",
                table: "PortalUsers",
                columns: new[] { "Issuer", "Subject" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuthenticationSessions");

            migrationBuilder.DropTable(
                name: "FirmMemberships");

            migrationBuilder.DropTable(
                name: "PersistedBoxAmounts");

            migrationBuilder.DropTable(
                name: "PersistedSourceBoxCodes");

            migrationBuilder.DropTable(
                name: "PersistedUnmappedBoxCodes");

            migrationBuilder.DropTable(
                name: "Firms");

            migrationBuilder.DropTable(
                name: "PortalUsers");

            migrationBuilder.DropTable(
                name: "PersistedLineEntries");

            migrationBuilder.DropTable(
                name: "PersistedSlips");

            migrationBuilder.DropTable(
                name: "PersistedReviewRecords");
        }
    }
}
