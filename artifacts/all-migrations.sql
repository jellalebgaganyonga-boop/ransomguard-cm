CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId" TEXT NOT NULL CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY,
    "ProductVersion" TEXT NOT NULL
);

BEGIN TRANSACTION;

CREATE TABLE "AgentStates" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_AgentStates" PRIMARY KEY,
    "State" TEXT NOT NULL,
    "AgentVersion" TEXT NOT NULL,
    "Hostname" TEXT NOT NULL,
    "Metadata" TEXT NULL,
    "CreatedAt" TEXT NOT NULL,
    "UpdatedAt" TEXT NOT NULL
);

CREATE TABLE "Alerts" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_Alerts" PRIMARY KEY,
    "Severity" TEXT NOT NULL,
    "Title" TEXT NOT NULL,
    "Description" TEXT NOT NULL,
    "SourceEventId" TEXT NULL,
    "Acknowledged" INTEGER NOT NULL,
    "Timestamp" TEXT NOT NULL,
    "CreatedAt" TEXT NOT NULL,
    "UpdatedAt" TEXT NOT NULL
);

CREATE TABLE "AuditLogs" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_AuditLogs" PRIMARY KEY,
    "Action" TEXT NOT NULL,
    "Details" TEXT NOT NULL,
    "EntityType" TEXT NULL,
    "EntityId" TEXT NULL,
    "PreviousHash" TEXT NULL,
    "CurrentHash" TEXT NOT NULL,
    "CreatedAt" TEXT NOT NULL,
    "UpdatedAt" TEXT NOT NULL
);

CREATE TABLE "DetectionEvents" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_DetectionEvents" PRIMARY KEY,
    "EventType" TEXT NOT NULL,
    "FilePath" TEXT NOT NULL,
    "OldFilePath" TEXT NULL,
    "Timestamp" TEXT NOT NULL,
    "CreatedAt" TEXT NOT NULL,
    "UpdatedAt" TEXT NOT NULL
);

CREATE INDEX "IX_Alerts_Severity" ON "Alerts" ("Severity");

CREATE INDEX "IX_Alerts_Timestamp" ON "Alerts" ("Timestamp");

CREATE INDEX "IX_AuditLogs_CreatedAt" ON "AuditLogs" ("CreatedAt");

CREATE INDEX "IX_DetectionEvents_FilePath" ON "DetectionEvents" ("FilePath");

CREATE INDEX "IX_DetectionEvents_Timestamp" ON "DetectionEvents" ("Timestamp");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260518004118_InitialCreate', '8.0.27');

COMMIT;

BEGIN TRANSACTION;

CREATE TABLE "CanaryAlerts" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_CanaryAlerts" PRIMARY KEY,
    "CanaryId" TEXT NOT NULL,
    "CanaryPath" TEXT NOT NULL,
    "AlertType" TEXT NOT NULL,
    "Severity" TEXT NOT NULL,
    "OffendingProcessId" INTEGER NULL,
    "OffendingProcessName" TEXT NULL,
    "OffendingProcessPath" TEXT NULL,
    "DetectedAt" TEXT NOT NULL,
    "CreatedAt" TEXT NOT NULL,
    "UpdatedAt" TEXT NOT NULL
);

CREATE TABLE "SentinelCanaries" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_SentinelCanaries" PRIMARY KEY,
    "FilePath" TEXT NOT NULL,
    "FileName" TEXT NOT NULL,
    "Directory" TEXT NOT NULL,
    "TemplateUsed" TEXT NOT NULL,
    "OriginalContentHash" TEXT NOT NULL,
    "FileSize" INTEGER NOT NULL,
    "Status" TEXT NOT NULL,
    "CreatedAt" TEXT NOT NULL,
    "LastCheckedAt" TEXT NOT NULL,
    "UpdatedAt" TEXT NOT NULL
);

CREATE INDEX "IX_CanaryAlerts_CanaryId" ON "CanaryAlerts" ("CanaryId");

CREATE INDEX "IX_CanaryAlerts_DetectedAt" ON "CanaryAlerts" ("DetectedAt");

CREATE INDEX "IX_SentinelCanaries_Directory" ON "SentinelCanaries" ("Directory");

CREATE UNIQUE INDEX "IX_SentinelCanaries_FilePath" ON "SentinelCanaries" ("FilePath");

CREATE INDEX "IX_SentinelCanaries_Status" ON "SentinelCanaries" ("Status");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260518011935_AddSentinelEntities', '8.0.27');

COMMIT;

BEGIN TRANSACTION;

ALTER TABLE "AuditLogs" ADD "Signature" TEXT NULL;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260518053238_AddAuditLogSignature', '8.0.27');

COMMIT;

BEGIN TRANSACTION;

CREATE TABLE "EntropyAlerts" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_EntropyAlerts" PRIMARY KEY,
    "FilePath" TEXT NOT NULL,
    "RuleId" INTEGER NOT NULL,
    "RuleName" TEXT NOT NULL,
    "Severity" TEXT NOT NULL,
    "BaselineEntropy" REAL NOT NULL,
    "CurrentEntropy" REAL NOT NULL,
    "Delta" REAL NOT NULL,
    "DetectedAt" TEXT NOT NULL,
    "CreatedAt" TEXT NOT NULL
);

CREATE TABLE "EntropyBaselines" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_EntropyBaselines" PRIMARY KEY,
    "FilePath" TEXT NOT NULL,
    "DirectoryPath" TEXT NOT NULL,
    "FileExtension" TEXT NOT NULL,
    "EntropyValue" REAL NOT NULL,
    "FileSize" INTEGER NOT NULL,
    "CapturedAt" TEXT NOT NULL,
    "LastVerifiedAt" TEXT NULL
);

CREATE INDEX "IX_EntropyAlerts_DetectedAt" ON "EntropyAlerts" ("DetectedAt");

CREATE INDEX "IX_EntropyBaselines_DirectoryPath" ON "EntropyBaselines" ("DirectoryPath");

CREATE INDEX "IX_EntropyBaselines_FileExtension" ON "EntropyBaselines" ("FileExtension");

CREATE UNIQUE INDEX "IX_EntropyBaselines_FilePath" ON "EntropyBaselines" ("FilePath");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260518092123_AddEntropyEntities', '8.0.27');

COMMIT;

BEGIN TRANSACTION;

CREATE TABLE "GenealogyRecords" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_GenealogyRecords" PRIMARY KEY,
    "AlertId" TEXT NOT NULL,
    "CapturedAt" TEXT NOT NULL,
    "ProcessTreeJson" TEXT NOT NULL,
    "SuspiciousPatternsJson" TEXT NOT NULL,
    "RootProcessId" INTEGER NOT NULL,
    "RootProcessName" TEXT NOT NULL,
    "Summary" TEXT NOT NULL
);

CREATE INDEX "IX_GenealogyRecords_AlertId" ON "GenealogyRecords" ("AlertId");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260518100157_AddGenealogyRecord', '8.0.27');

COMMIT;

BEGIN TRANSACTION;

CREATE TABLE "UsbWhitelistEntries" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_UsbWhitelistEntries" PRIMARY KEY,
    "SerialNumberHash" TEXT NOT NULL,
    "Description" TEXT NOT NULL,
    "AddedByUser" TEXT NOT NULL,
    "AddedAt" TEXT NOT NULL,
    "ExpiresAt" TEXT NULL,
    "IsActive" INTEGER NOT NULL,
    "PolicyLevel" TEXT NOT NULL
);

CREATE INDEX "IX_UsbWhitelistEntries_IsActive" ON "UsbWhitelistEntries" ("IsActive");

CREATE INDEX "IX_UsbWhitelistEntries_SerialNumberHash" ON "UsbWhitelistEntries" ("SerialNumberHash");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260523010000_AddUsbWhitelist', '8.0.27');

COMMIT;

BEGIN TRANSACTION;

CREATE TABLE "UsbPolicies" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_UsbPolicies" PRIMARY KEY,
    "Mode" TEXT NOT NULL,
    "MaxFileSizeForScanMB" INTEGER NOT NULL,
    "MaxScanDurationSeconds" INTEGER NOT NULL,
    "ScanArchiveContents" INTEGER NOT NULL,
    "SuspiciousExtensionsJson" TEXT NOT NULL,
    "BlockBootableUsb" INTEGER NOT NULL,
    "AlertOnHidDevice" INTEGER NOT NULL,
    "AlertOnNetworkDevice" INTEGER NOT NULL,
    "UpdatedAt" TEXT NOT NULL
);

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260523010100_AddUsbPolicy', '8.0.27');

COMMIT;

BEGIN TRANSACTION;

CREATE TABLE "UsbConnectionLogs" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_UsbConnectionLogs" PRIMARY KEY,
    "DeviceInstanceId" TEXT NOT NULL,
    "SerialNumberHash" TEXT NOT NULL,
    "VendorId" TEXT NOT NULL,
    "ProductId" TEXT NOT NULL,
    "DeviceClass" TEXT NOT NULL,
    "DriveLetter" TEXT NULL,
    "ConnectedAt" TEXT NOT NULL,
    "DisconnectedAt" TEXT NULL,
    "WasWhitelisted" INTEGER NOT NULL,
    "RetainUntil" TEXT NOT NULL
);

CREATE INDEX "IX_UsbConnectionLogs_ConnectedAt" ON "UsbConnectionLogs" ("ConnectedAt");

CREATE INDEX "IX_UsbConnectionLogs_RetainUntil" ON "UsbConnectionLogs" ("RetainUntil");

CREATE INDEX "IX_UsbConnectionLogs_SerialNumberHash" ON "UsbConnectionLogs" ("SerialNumberHash");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260523010200_AddUsbConnectionLog', '8.0.27');

COMMIT;

BEGIN TRANSACTION;

CREATE TABLE "UsbScanResults" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_UsbScanResults" PRIMARY KEY,
    "UsbConnectionLogId" TEXT NOT NULL,
    "TotalFilesScanned" INTEGER NOT NULL,
    "FlaggedFilesCount" INTEGER NOT NULL,
    "FlaggedFilesJson" TEXT NOT NULL,
    "ScanDurationMs" INTEGER NOT NULL,
    "ScanCompleted" INTEGER NOT NULL,
    "HighestSeverity" TEXT NOT NULL,
    "ScannedAt" TEXT NOT NULL
);

CREATE INDEX "IX_UsbScanResults_ScannedAt" ON "UsbScanResults" ("ScannedAt");

CREATE INDEX "IX_UsbScanResults_UsbConnectionLogId" ON "UsbScanResults" ("UsbConnectionLogId");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260523010300_AddUsbScanResult', '8.0.27');

COMMIT;

BEGIN TRANSACTION;

CREATE TABLE "UsbAlerts" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_UsbAlerts" PRIMARY KEY,
    "UsbScanResultId" TEXT NOT NULL,
    "AlertId" TEXT NULL,
    "Title" TEXT NOT NULL,
    "Description" TEXT NOT NULL,
    "Severity" TEXT NOT NULL,
    "ActionTaken" TEXT NOT NULL,
    "GeneratedAt" TEXT NOT NULL,
    "GenealogyId" TEXT NULL
);

CREATE INDEX "IX_UsbAlerts_GeneratedAt" ON "UsbAlerts" ("GeneratedAt");

CREATE INDEX "IX_UsbAlerts_UsbScanResultId" ON "UsbAlerts" ("UsbScanResultId");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260523010400_AddUsbAlert', '8.0.27');

COMMIT;

BEGIN TRANSACTION;

CREATE TABLE "QuarantinedFiles" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_QuarantinedFiles" PRIMARY KEY,
    "OriginalPath" TEXT NOT NULL,
    "QuarantinePath" TEXT NOT NULL,
    "OriginalSha256" TEXT NOT NULL,
    "QuarantineSha256" TEXT NOT NULL,
    "OriginalSize" INTEGER NOT NULL,
    "QuarantineReason" TEXT NOT NULL,
    "Severity" TEXT NOT NULL,
    "SourceUsbSerial" TEXT NOT NULL,
    "QuarantinedAt" TEXT NOT NULL,
    "QuarantinedByUser" TEXT NOT NULL,
    "RestoredAt" TEXT NULL,
    "RestoredByUser" TEXT NULL,
    "RetainUntil" TEXT NOT NULL,
    "IsEncrypted" INTEGER NOT NULL
);

CREATE INDEX "IX_QuarantinedFiles_QuarantinedAt" ON "QuarantinedFiles" ("QuarantinedAt");

CREATE INDEX "IX_QuarantinedFiles_RetainUntil" ON "QuarantinedFiles" ("RetainUntil");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260523010500_AddQuarantine', '8.0.27');

COMMIT;

BEGIN TRANSACTION;

CREATE TABLE "ExfilAlerts" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_ExfilAlerts" PRIMARY KEY,
    "RuleName" TEXT NOT NULL,
    "Severity" TEXT NOT NULL,
    "ProcessId" INTEGER NOT NULL,
    "ProcessName" TEXT NOT NULL,
    "Destination" TEXT NOT NULL,
    "DestinationPort" INTEGER NULL,
    "BytesTransferred" INTEGER NOT NULL,
    "Description" TEXT NOT NULL,
    "ActionTaken" TEXT NOT NULL,
    "DetectedAt" TEXT NOT NULL,
    "GenealogyId" TEXT NULL,
    "CrossLinkedEntropyAlertId" TEXT NULL
);

CREATE INDEX "IX_ExfilAlerts_DetectedAt" ON "ExfilAlerts" ("DetectedAt");

CREATE INDEX "IX_ExfilAlerts_ProcessName" ON "ExfilAlerts" ("ProcessName");

CREATE INDEX "IX_ExfilAlerts_RuleName" ON "ExfilAlerts" ("RuleName");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260523020000_AddExfilAlert', '8.0.27');

COMMIT;

BEGIN TRANSACTION;

CREATE TABLE "NetworkBaselines" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_NetworkBaselines" PRIMARY KEY,
    "Scope" TEXT NOT NULL,
    "Phase" TEXT NOT NULL,
    "LearningStartedAt" TEXT NOT NULL,
    "LearningCompletedAt" TEXT NULL,
    "DriftDetectedAt" TEXT NULL,
    "ObservationCount" INTEGER NOT NULL,
    "ConfidenceScore" REAL NOT NULL,
    "LastUpdatedAt" TEXT NOT NULL
);

CREATE INDEX "IX_NetworkBaselines_Phase" ON "NetworkBaselines" ("Phase");

CREATE UNIQUE INDEX "IX_NetworkBaselines_Scope" ON "NetworkBaselines" ("Scope");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260523020100_AddNetworkBaseline', '8.0.27');

COMMIT;

BEGIN TRANSACTION;

CREATE TABLE "NetworkBaselineMetrics" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_NetworkBaselineMetrics" PRIMARY KEY,
    "NetworkBaselineId" TEXT NOT NULL,
    "MetricType" TEXT NOT NULL,
    "Dimension" TEXT NOT NULL,
    "HourlyAverageBytes" REAL NOT NULL,
    "HourlyStdDevBytes" REAL NOT NULL,
    "DailyAverageBytes" REAL NOT NULL,
    "HourlyPatternJson" TEXT NOT NULL,
    "WeeklyPatternJson" TEXT NOT NULL,
    "FirstSeenAt" TEXT NOT NULL,
    "LastUpdatedAt" TEXT NOT NULL,
    "ObservationCount" INTEGER NOT NULL,
    "ConfidenceScore" REAL NOT NULL
);

CREATE INDEX "IX_NetworkBaselineMetrics_NetworkBaselineId" ON "NetworkBaselineMetrics" ("NetworkBaselineId");

CREATE UNIQUE INDEX "IX_NetworkBaselineMetrics_NetworkBaselineId_MetricType_Dimension" ON "NetworkBaselineMetrics" ("NetworkBaselineId", "MetricType", "Dimension");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260523020200_AddNetworkBaselineMetric', '8.0.27');

COMMIT;

BEGIN TRANSACTION;

CREATE TABLE "IndicatorRemovalEvents" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_IndicatorRemovalEvents" PRIMARY KEY,
    "EventType" TEXT NOT NULL,
    "MitreTechniqueId" TEXT NOT NULL,
    "ProcessId" INTEGER NOT NULL,
    "ProcessName" TEXT NOT NULL,
    "CommandLine" TEXT NOT NULL,
    "TargetResource" TEXT NOT NULL,
    "Severity" TEXT NOT NULL,
    "Description" TEXT NOT NULL,
    "ActionTaken" TEXT NOT NULL,
    "DetectedAt" TEXT NOT NULL,
    "GenealogyId" TEXT NULL,
    "WhitelistSuppressed" INTEGER NOT NULL,
    "KillChainCorrelationId" TEXT NULL
);

CREATE INDEX "IX_IndicatorRemovalEvents_DetectedAt" ON "IndicatorRemovalEvents" ("DetectedAt");

CREATE INDEX "IX_IndicatorRemovalEvents_EventType" ON "IndicatorRemovalEvents" ("EventType");

CREATE INDEX "IX_IndicatorRemovalEvents_KillChainCorrelationId" ON "IndicatorRemovalEvents" ("KillChainCorrelationId");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260523030000_AddIndicatorRemovalEvent', '8.0.27');

COMMIT;

