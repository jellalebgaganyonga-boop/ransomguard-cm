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

