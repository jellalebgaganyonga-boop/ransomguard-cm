BEGIN TRANSACTION;

CREATE TABLE "IronCladDeviceStates" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_IronCladDeviceStates" PRIMARY KEY,
    "PortNumber" INTEGER NOT NULL,
    "State" TEXT NOT NULL,
    "LastChangedAt" TEXT NOT NULL,
    "LastEventId" TEXT NULL,
    "Reason" TEXT NOT NULL
);

CREATE TABLE "IronCladEvents" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_IronCladEvents" PRIMARY KEY,
    "CommandId" TEXT NOT NULL,
    "Action" TEXT NOT NULL,
    "Parameter" TEXT NOT NULL,
    "Justification" TEXT NOT NULL,
    "SourceAlertId" TEXT NULL,
    "IssuedAt" TEXT NOT NULL,
    "CompletedAt" TEXT NULL,
    "Outcome" TEXT NOT NULL,
    "ResponsePayload" TEXT NULL,
    "ErrorMessage" TEXT NULL,
    "IssuedByUser" TEXT NOT NULL
);

CREATE INDEX "IX_IronCladDeviceStates_LastChangedAt" ON "IronCladDeviceStates" ("LastChangedAt");

CREATE UNIQUE INDEX "IX_IronCladDeviceStates_PortNumber" ON "IronCladDeviceStates" ("PortNumber");

CREATE INDEX "IX_IronCladEvents_CommandId" ON "IronCladEvents" ("CommandId");

CREATE INDEX "IX_IronCladEvents_IssuedAt" ON "IronCladEvents" ("IssuedAt");

CREATE INDEX "IX_IronCladEvents_SourceAlertId" ON "IronCladEvents" ("SourceAlertId");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260528232112_AddIronCladEvent', '8.0.27');

COMMIT;

