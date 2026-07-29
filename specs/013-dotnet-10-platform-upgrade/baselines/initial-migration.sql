IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
GO

CREATE TABLE [Firms] (
    [FirmId] uniqueidentifier NOT NULL,
    [DisplayName] nvarchar(200) NOT NULL,
    [CreatedAtUtc] datetimeoffset NOT NULL,
    [UpdatedAtUtc] datetimeoffset NOT NULL,
    CONSTRAINT [PK_Firms] PRIMARY KEY ([FirmId])
);
GO

CREATE TABLE [PersistedReviewRecords] (
    [RecordId] uniqueidentifier NOT NULL,
    [FirmId] uniqueidentifier NOT NULL,
    [CaseId] nvarchar(64) COLLATE Latin1_General_BIN2 NOT NULL,
    [ParticipantId] nvarchar(64) COLLATE Latin1_General_BIN2 NOT NULL,
    [TaxYear] int NOT NULL,
    [PackageVersion] int NOT NULL,
    [CreatedAtUtc] datetimeoffset NOT NULL,
    [UpdatedAtUtc] datetimeoffset NOT NULL,
    [RetentionUntilUtc] datetimeoffset NOT NULL,
    CONSTRAINT [PK_PersistedReviewRecords] PRIMARY KEY ([FirmId], [RecordId])
);
DECLARE @defaultSchema AS sysname;
SET @defaultSchema = SCHEMA_NAME();
DECLARE @description AS sql_variant;
SET @description = N'Exclusive UTC retention boundary: the record is purge-eligible at or after this instant.';
EXEC sp_addextendedproperty 'MS_Description', @description, 'SCHEMA', @defaultSchema, 'TABLE', N'PersistedReviewRecords', 'COLUMN', N'RetentionUntilUtc';
GO

CREATE TABLE [PortalUsers] (
    [UserId] uniqueidentifier NOT NULL,
    [Issuer] nvarchar(256) COLLATE Latin1_General_BIN2 NOT NULL,
    [Subject] nvarchar(256) COLLATE Latin1_General_BIN2 NOT NULL,
    [IsActive] bit NOT NULL,
    [CreatedAtUtc] datetimeoffset NOT NULL,
    [UpdatedAtUtc] datetimeoffset NOT NULL,
    CONSTRAINT [PK_PortalUsers] PRIMARY KEY ([UserId])
);
GO

CREATE TABLE [PersistedSlips] (
    [SlipId] uniqueidentifier NOT NULL,
    [FirmId] uniqueidentifier NOT NULL,
    [RecordId] uniqueidentifier NOT NULL,
    [Ordinal] int NOT NULL,
    [SlipTypeCode] nvarchar(8) COLLATE Latin1_General_BIN2 NOT NULL,
    [DocumentReference] uniqueidentifier NOT NULL,
    [TaxpayerDisplayName] nvarchar(200) NOT NULL,
    [EmployerDisplayName] nvarchar(200) NOT NULL,
    CONSTRAINT [PK_PersistedSlips] PRIMARY KEY ([FirmId], [SlipId]),
    CONSTRAINT [FK_PersistedSlips_PersistedReviewRecords_FirmId_RecordId] FOREIGN KEY ([FirmId], [RecordId]) REFERENCES [PersistedReviewRecords] ([FirmId], [RecordId]) ON DELETE CASCADE
);
GO

CREATE TABLE [AuthenticationSessions] (
    [SessionId] uniqueidentifier NOT NULL,
    [UserId] uniqueidentifier NOT NULL,
    [ActiveFirmId] uniqueidentifier NULL,
    [CreatedAtUtc] datetimeoffset NOT NULL,
    [LastSeenAtUtc] datetimeoffset NOT NULL,
    [IdleExpiresAtUtc] datetimeoffset NOT NULL,
    [AbsoluteExpiresAtUtc] datetimeoffset NOT NULL,
    [RevokedAtUtc] datetimeoffset NULL,
    CONSTRAINT [PK_AuthenticationSessions] PRIMARY KEY ([SessionId]),
    CONSTRAINT [FK_AuthenticationSessions_Firms_ActiveFirmId] FOREIGN KEY ([ActiveFirmId]) REFERENCES [Firms] ([FirmId]) ON DELETE NO ACTION,
    CONSTRAINT [FK_AuthenticationSessions_PortalUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [PortalUsers] ([UserId]) ON DELETE NO ACTION
);
GO

CREATE TABLE [FirmMemberships] (
    [MembershipId] uniqueidentifier NOT NULL,
    [FirmId] uniqueidentifier NOT NULL,
    [UserId] uniqueidentifier NOT NULL,
    [IsActive] bit NOT NULL,
    [CreatedAtUtc] datetimeoffset NOT NULL,
    [UpdatedAtUtc] datetimeoffset NOT NULL,
    CONSTRAINT [PK_FirmMemberships] PRIMARY KEY ([MembershipId]),
    CONSTRAINT [FK_FirmMemberships_Firms_FirmId] FOREIGN KEY ([FirmId]) REFERENCES [Firms] ([FirmId]) ON DELETE NO ACTION,
    CONSTRAINT [FK_FirmMemberships_PortalUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [PortalUsers] ([UserId]) ON DELETE NO ACTION
);
GO

CREATE TABLE [PersistedBoxAmounts] (
    [BoxAmountId] uniqueidentifier NOT NULL,
    [FirmId] uniqueidentifier NOT NULL,
    [SlipId] uniqueidentifier NOT NULL,
    [Ordinal] int NOT NULL,
    [BoxCode] nvarchar(16) COLLATE Latin1_General_BIN2 NOT NULL,
    [Amount] decimal(19,4) NOT NULL,
    CONSTRAINT [PK_PersistedBoxAmounts] PRIMARY KEY ([FirmId], [BoxAmountId]),
    CONSTRAINT [FK_PersistedBoxAmounts_PersistedSlips_FirmId_SlipId] FOREIGN KEY ([FirmId], [SlipId]) REFERENCES [PersistedSlips] ([FirmId], [SlipId]) ON DELETE CASCADE
);
GO

CREATE TABLE [PersistedLineEntries] (
    [LineEntryId] uniqueidentifier NOT NULL,
    [FirmId] uniqueidentifier NOT NULL,
    [SlipId] uniqueidentifier NOT NULL,
    [Ordinal] int NOT NULL,
    [LineCode] nvarchar(16) COLLATE Latin1_General_BIN2 NOT NULL,
    [Amount] decimal(19,4) NOT NULL,
    CONSTRAINT [PK_PersistedLineEntries] PRIMARY KEY ([FirmId], [LineEntryId]),
    CONSTRAINT [FK_PersistedLineEntries_PersistedSlips_FirmId_SlipId] FOREIGN KEY ([FirmId], [SlipId]) REFERENCES [PersistedSlips] ([FirmId], [SlipId]) ON DELETE CASCADE
);
GO

CREATE TABLE [PersistedUnmappedBoxCodes] (
    [UnmappedBoxCodeId] uniqueidentifier NOT NULL,
    [FirmId] uniqueidentifier NOT NULL,
    [SlipId] uniqueidentifier NOT NULL,
    [Ordinal] int NOT NULL,
    [BoxCode] nvarchar(16) COLLATE Latin1_General_BIN2 NOT NULL,
    CONSTRAINT [PK_PersistedUnmappedBoxCodes] PRIMARY KEY ([FirmId], [UnmappedBoxCodeId]),
    CONSTRAINT [FK_PersistedUnmappedBoxCodes_PersistedSlips_FirmId_SlipId] FOREIGN KEY ([FirmId], [SlipId]) REFERENCES [PersistedSlips] ([FirmId], [SlipId]) ON DELETE CASCADE
);
GO

CREATE TABLE [PersistedSourceBoxCodes] (
    [SourceBoxCodeId] uniqueidentifier NOT NULL,
    [FirmId] uniqueidentifier NOT NULL,
    [LineEntryId] uniqueidentifier NOT NULL,
    [Ordinal] int NOT NULL,
    [BoxCode] nvarchar(16) COLLATE Latin1_General_BIN2 NOT NULL,
    CONSTRAINT [PK_PersistedSourceBoxCodes] PRIMARY KEY ([FirmId], [SourceBoxCodeId]),
    CONSTRAINT [FK_PersistedSourceBoxCodes_PersistedLineEntries_FirmId_LineEntryId] FOREIGN KEY ([FirmId], [LineEntryId]) REFERENCES [PersistedLineEntries] ([FirmId], [LineEntryId]) ON DELETE CASCADE
);
GO

CREATE INDEX [IX_AuthenticationSessions_ActiveFirmId] ON [AuthenticationSessions] ([ActiveFirmId]);
GO

CREATE INDEX [IX_AuthenticationSessions_UserId_RevokedAtUtc] ON [AuthenticationSessions] ([UserId], [RevokedAtUtc]);
GO

CREATE UNIQUE INDEX [IX_FirmMemberships_FirmId_UserId] ON [FirmMemberships] ([FirmId], [UserId]);
GO

CREATE INDEX [IX_FirmMemberships_UserId_IsActive_FirmId] ON [FirmMemberships] ([UserId], [IsActive], [FirmId]);
GO

CREATE UNIQUE INDEX [IX_PersistedBoxAmounts_FirmId_SlipId_Ordinal] ON [PersistedBoxAmounts] ([FirmId], [SlipId], [Ordinal]);
GO

CREATE UNIQUE INDEX [IX_PersistedLineEntries_FirmId_SlipId_Ordinal] ON [PersistedLineEntries] ([FirmId], [SlipId], [Ordinal]);
GO

CREATE UNIQUE INDEX [IX_PersistedReviewRecords_FirmId_CaseId_ParticipantId_TaxYear_PackageVersion] ON [PersistedReviewRecords] ([FirmId], [CaseId], [ParticipantId], [TaxYear], [PackageVersion]);
GO

CREATE INDEX [IX_PersistedReviewRecords_FirmId_RetentionUntilUtc] ON [PersistedReviewRecords] ([FirmId], [RetentionUntilUtc]);
GO

CREATE UNIQUE INDEX [IX_PersistedSlips_FirmId_RecordId_Ordinal] ON [PersistedSlips] ([FirmId], [RecordId], [Ordinal]);
GO

CREATE UNIQUE INDEX [IX_PersistedSourceBoxCodes_FirmId_LineEntryId_Ordinal] ON [PersistedSourceBoxCodes] ([FirmId], [LineEntryId], [Ordinal]);
GO

CREATE UNIQUE INDEX [IX_PersistedUnmappedBoxCodes_FirmId_SlipId_Ordinal] ON [PersistedUnmappedBoxCodes] ([FirmId], [SlipId], [Ordinal]);
GO

CREATE UNIQUE INDEX [IX_PortalUsers_Issuer_Subject] ON [PortalUsers] ([Issuer], [Subject]);
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260729021907_InitialPersistence', N'8.0.0');
GO

COMMIT;
GO
