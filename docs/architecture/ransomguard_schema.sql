-- =============================================================================
-- RansomGuard-CM — Database Schema (MySQL 8.0+)
-- =============================================================================
-- Project        : RansomGuard-CM Anti-Ransomware Platform
-- Author         : Jella Lebga (Kuate Abdel Yaniv)
-- Institution    : ICT University Yaoundé — Faculty of ICT
-- Schema Version : 1.0.0
-- Last Updated   : May 2026
-- 
-- Design Principles (Senior Google-style):
--   1. UUIDs as primary keys (no enumeration attacks, federation-ready)
--   2. Soft deletes with deleted_at (Loi 2024/017 compliance)
--   3. Optimistic locking with version columns
--   4. UTC timestamps with microsecond precision
--   5. Append-only audit log with hash chain
--   6. Strategic composite indexes for dashboard queries
--   7. CHARACTER SET utf8mb4 for emoji and full Unicode
--   8. ENGINE InnoDB for ACID guarantees
--   9. ROW_FORMAT=DYNAMIC for variable-length efficiency
--  10. Strict foreign key enforcement
-- =============================================================================

-- Database creation with proper character set
CREATE DATABASE IF NOT EXISTS ransomguard_cm
    CHARACTER SET utf8mb4
    COLLATE utf8mb4_unicode_ci;

USE ransomguard_cm;

-- Enable strict mode for data integrity
SET sql_mode = 'STRICT_TRANS_TABLES,NO_ZERO_IN_DATE,NO_ZERO_DATE,ERROR_FOR_DIVISION_BY_ZERO,NO_ENGINE_SUBSTITUTION';

-- =============================================================================
-- DOMAIN 1: TENANCY
-- =============================================================================

CREATE TABLE hospitals (
    id                  CHAR(36)        NOT NULL,
    slug                VARCHAR(50)     NOT NULL,
    name                VARCHAR(200)    NOT NULL,
    country_code        CHAR(2)         NOT NULL DEFAULT 'CM',
    city                VARCHAR(100)    NOT NULL,
    type                ENUM('public', 'private', 'confessional', 'university') NOT NULL,
    bed_capacity        INT UNSIGNED    DEFAULT NULL,
    contact_email       VARCHAR(255)    NOT NULL,
    contact_phone       VARCHAR(50)     DEFAULT NULL,
    antic_registration  VARCHAR(100)    DEFAULT NULL,
    address_line1       VARCHAR(255)    DEFAULT NULL,
    region              VARCHAR(100)    DEFAULT NULL,
    timezone            VARCHAR(50)     NOT NULL DEFAULT 'Africa/Douala',
    locale              VARCHAR(10)     NOT NULL DEFAULT 'fr-CM',
    created_at          TIMESTAMP(6)    NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    updated_at          TIMESTAMP(6)    NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
    deleted_at          TIMESTAMP(6)    DEFAULT NULL,
    version             INT UNSIGNED    NOT NULL DEFAULT 1,
    PRIMARY KEY (id),
    UNIQUE KEY uq_hospitals_slug (slug),
    UNIQUE KEY uq_hospitals_email (contact_email),
    KEY idx_hospitals_country_city (country_code, city),
    KEY idx_hospitals_deleted_at (deleted_at)
) ENGINE=InnoDB ROW_FORMAT=DYNAMIC COMMENT='Multi-tenant root entity — one row per protected hospital';


CREATE TABLE subscriptions (
    id              CHAR(36)        NOT NULL,
    hospital_id     CHAR(36)        NOT NULL,
    plan_type       ENUM('starter', 'standard', 'enterprise') NOT NULL,
    max_endpoints   INT UNSIGNED    NOT NULL,
    starts_at       TIMESTAMP(6)    NOT NULL,
    ends_at         TIMESTAMP(6)    NOT NULL,
    status          ENUM('active', 'grace', 'expired', 'suspended', 'cancelled') NOT NULL DEFAULT 'active',
    billing_cycle   ENUM('monthly', 'annual') NOT NULL DEFAULT 'annual',
    amount_fcfa     DECIMAL(12,2)   NOT NULL,
    created_at      TIMESTAMP(6)    NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    updated_at      TIMESTAMP(6)    NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
    version         INT UNSIGNED    NOT NULL DEFAULT 1,
    PRIMARY KEY (id),
    KEY idx_subscriptions_hospital_status (hospital_id, status),
    KEY idx_subscriptions_ends_at (ends_at),
    CONSTRAINT fk_subscriptions_hospital
        FOREIGN KEY (hospital_id) REFERENCES hospitals(id)
        ON DELETE RESTRICT ON UPDATE CASCADE,
    CONSTRAINT chk_subscriptions_dates CHECK (ends_at > starts_at)
) ENGINE=InnoDB ROW_FORMAT=DYNAMIC COMMENT='Hospital subscriptions to RansomGuard service tiers';


-- =============================================================================
-- DOMAIN 2: IDENTITY
-- =============================================================================

CREATE TABLE users (
    id                  CHAR(36)        NOT NULL,
    hospital_id         CHAR(36)        NOT NULL,
    email               VARCHAR(255)    NOT NULL,
    full_name           VARCHAR(200)    NOT NULL,
    password_hash       VARCHAR(255)    NOT NULL,
    role                ENUM('admin', 'technician', 'director', 'auditor', 'support') NOT NULL,
    is_active           BOOLEAN         NOT NULL DEFAULT TRUE,
    email_verified_at   TIMESTAMP(6)    DEFAULT NULL,
    last_login_at       TIMESTAMP(6)    DEFAULT NULL,
    last_login_ip       VARCHAR(45)     DEFAULT NULL,
    failed_login_count  INT UNSIGNED    NOT NULL DEFAULT 0,
    locked_until        TIMESTAMP(6)    DEFAULT NULL,
    password_changed_at TIMESTAMP(6)    NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    preferred_locale    VARCHAR(10)     NOT NULL DEFAULT 'fr-CM',
    created_at          TIMESTAMP(6)    NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    updated_at          TIMESTAMP(6)    NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
    deleted_at          TIMESTAMP(6)    DEFAULT NULL,
    version             INT UNSIGNED    NOT NULL DEFAULT 1,
    PRIMARY KEY (id),
    UNIQUE KEY uq_users_email (email),
    KEY idx_users_hospital_role (hospital_id, role, is_active),
    KEY idx_users_deleted_at (deleted_at),
    CONSTRAINT fk_users_hospital
        FOREIGN KEY (hospital_id) REFERENCES hospitals(id)
        ON DELETE RESTRICT ON UPDATE CASCADE
) ENGINE=InnoDB ROW_FORMAT=DYNAMIC COMMENT='User accounts with RBAC role assignment';


CREATE TABLE mfa_devices (
    id                  CHAR(36)        NOT NULL,
    user_id             CHAR(36)        NOT NULL,
    device_type         ENUM('totp', 'backup_codes', 'webauthn') NOT NULL,
    secret_encrypted    VARBINARY(255)  NOT NULL,
    device_label        VARCHAR(100)    DEFAULT NULL,
    is_primary          BOOLEAN         NOT NULL DEFAULT FALSE,
    last_used_at        TIMESTAMP(6)    DEFAULT NULL,
    created_at          TIMESTAMP(6)    NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    revoked_at          TIMESTAMP(6)    DEFAULT NULL,
    PRIMARY KEY (id),
    KEY idx_mfa_user_active (user_id, revoked_at),
    CONSTRAINT fk_mfa_user
        FOREIGN KEY (user_id) REFERENCES users(id)
        ON DELETE CASCADE ON UPDATE CASCADE
) ENGINE=InnoDB ROW_FORMAT=DYNAMIC COMMENT='Multi-factor authentication devices per user';


CREATE TABLE sessions (
    id              CHAR(36)        NOT NULL,
    user_id         CHAR(36)        NOT NULL,
    token_hash      VARCHAR(64)     NOT NULL,
    refresh_hash    VARCHAR(64)     DEFAULT NULL,
    ip_address      VARCHAR(45)     NOT NULL,
    user_agent      VARCHAR(500)    DEFAULT NULL,
    expires_at      TIMESTAMP(6)    NOT NULL,
    revoked_at      TIMESTAMP(6)    DEFAULT NULL,
    created_at      TIMESTAMP(6)    NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    PRIMARY KEY (id),
    UNIQUE KEY uq_sessions_token (token_hash),
    KEY idx_sessions_user_active (user_id, revoked_at, expires_at),
    KEY idx_sessions_expires (expires_at),
    CONSTRAINT fk_sessions_user
        FOREIGN KEY (user_id) REFERENCES users(id)
        ON DELETE CASCADE ON UPDATE CASCADE
) ENGINE=InnoDB ROW_FORMAT=DYNAMIC COMMENT='Active user sessions with JWT token tracking';


-- =============================================================================
-- DOMAIN 3: ASSET (Endpoints and Agents)
-- =============================================================================

CREATE TABLE endpoints (
    id                      CHAR(36)        NOT NULL,
    hospital_id             CHAR(36)        NOT NULL,
    hostname                VARCHAR(255)    NOT NULL,
    fqdn                    VARCHAR(255)    DEFAULT NULL,
    ip_address              VARCHAR(45)     DEFAULT NULL,
    mac_address             VARCHAR(17)     DEFAULT NULL,
    os_name                 VARCHAR(100)    NOT NULL,
    os_version              VARCHAR(50)     NOT NULL,
    os_architecture         ENUM('x86', 'x64', 'arm64') NOT NULL DEFAULT 'x64',
    department              VARCHAR(100)    DEFAULT NULL,
    location                VARCHAR(255)    DEFAULT NULL,
    criticality             ENUM('low', 'medium', 'high', 'critical') NOT NULL DEFAULT 'medium',
    is_medical_critical     BOOLEAN         NOT NULL DEFAULT FALSE,
    medical_software        JSON            DEFAULT NULL,
    assigned_user           VARCHAR(255)    DEFAULT NULL,
    first_seen_at           TIMESTAMP(6)    NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    last_seen_at            TIMESTAMP(6)    NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    status                  ENUM('active', 'offline', 'quarantined', 'decommissioned') NOT NULL DEFAULT 'active',
    created_at              TIMESTAMP(6)    NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    updated_at              TIMESTAMP(6)    NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
    deleted_at              TIMESTAMP(6)    DEFAULT NULL,
    version                 INT UNSIGNED    NOT NULL DEFAULT 1,
    PRIMARY KEY (id),
    UNIQUE KEY uq_endpoints_hospital_hostname (hospital_id, hostname),
    KEY idx_endpoints_hospital_status (hospital_id, status),
    KEY idx_endpoints_criticality (is_medical_critical, criticality),
    KEY idx_endpoints_last_seen (last_seen_at),
    KEY idx_endpoints_deleted_at (deleted_at),
    CONSTRAINT fk_endpoints_hospital
        FOREIGN KEY (hospital_id) REFERENCES hospitals(id)
        ON DELETE RESTRICT ON UPDATE CASCADE
) ENGINE=InnoDB ROW_FORMAT=DYNAMIC COMMENT='Protected Windows endpoints — one row per workstation';


CREATE TABLE agents (
    id                      CHAR(36)        NOT NULL,
    endpoint_id             CHAR(36)        NOT NULL,
    agent_uuid              VARCHAR(36)     NOT NULL,
    version                 VARCHAR(20)     NOT NULL,
    certificate_fingerprint VARCHAR(64)     NOT NULL,
    certificate_pem         TEXT            NOT NULL,
    public_key              TEXT            NOT NULL,
    revoked                 BOOLEAN         NOT NULL DEFAULT FALSE,
    last_heartbeat_at       TIMESTAMP(6)    DEFAULT NULL,
    last_telemetry_at       TIMESTAMP(6)    DEFAULT NULL,
    installed_at            TIMESTAMP(6)    NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    configuration_hash      VARCHAR(64)     DEFAULT NULL,
    health_status           ENUM('healthy', 'degraded', 'unhealthy', 'unknown') NOT NULL DEFAULT 'unknown',
    memory_usage_mb         INT UNSIGNED    DEFAULT NULL,
    cpu_usage_percent       DECIMAL(5,2)    DEFAULT NULL,
    PRIMARY KEY (id),
    UNIQUE KEY uq_agents_endpoint (endpoint_id),
    UNIQUE KEY uq_agents_uuid (agent_uuid),
    UNIQUE KEY uq_agents_cert_fingerprint (certificate_fingerprint),
    KEY idx_agents_heartbeat (last_heartbeat_at, health_status),
    CONSTRAINT fk_agents_endpoint
        FOREIGN KEY (endpoint_id) REFERENCES endpoints(id)
        ON DELETE CASCADE ON UPDATE CASCADE
) ENGINE=InnoDB ROW_FORMAT=DYNAMIC COMMENT='Agent installations — 1:1 with endpoints';


-- =============================================================================
-- DOMAIN 4: DETECTION
-- =============================================================================

CREATE TABLE incidents (
    id                  CHAR(36)        NOT NULL,
    hospital_id         CHAR(36)        NOT NULL,
    endpoint_id         CHAR(36)        NOT NULL,
    incident_number     BIGINT UNSIGNED AUTO_INCREMENT,
    severity            ENUM('low', 'medium', 'high', 'critical') NOT NULL,
    category            ENUM('ransomware', 'suspicious_process', 'usb_threat', 'exfiltration', 'policy_violation', 'unknown') NOT NULL,
    title               VARCHAR(255)    NOT NULL,
    description         TEXT            DEFAULT NULL,
    detected_at         TIMESTAMP(6)    NOT NULL,
    contained_at        TIMESTAMP(6)    DEFAULT NULL,
    resolved_at         TIMESTAMP(6)    DEFAULT NULL,
    signal_count        INT UNSIGNED    NOT NULL DEFAULT 0,
    signals             JSON            DEFAULT NULL,
    actions_taken       JSON            DEFAULT NULL,
    files_affected      INT UNSIGNED    DEFAULT 0,
    status              ENUM('detected', 'contained', 'investigating', 'resolved', 'false_positive') NOT NULL DEFAULT 'detected',
    acknowledged_by     CHAR(36)        DEFAULT NULL,
    acknowledged_at     TIMESTAMP(6)    DEFAULT NULL,
    resolution_notes    TEXT            DEFAULT NULL,
    report_pdf_url      VARCHAR(500)    DEFAULT NULL,
    created_at          TIMESTAMP(6)    NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    updated_at          TIMESTAMP(6)    NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
    version             INT UNSIGNED    NOT NULL DEFAULT 1,
    PRIMARY KEY (id),
    UNIQUE KEY uq_incidents_number (incident_number),
    KEY idx_incidents_hospital_status (hospital_id, status, detected_at DESC),
    KEY idx_incidents_endpoint (endpoint_id, detected_at DESC),
    KEY idx_incidents_severity (severity, detected_at DESC),
    KEY idx_incidents_category (category, detected_at DESC),
    CONSTRAINT fk_incidents_hospital
        FOREIGN KEY (hospital_id) REFERENCES hospitals(id)
        ON DELETE RESTRICT ON UPDATE CASCADE,
    CONSTRAINT fk_incidents_endpoint
        FOREIGN KEY (endpoint_id) REFERENCES endpoints(id)
        ON DELETE RESTRICT ON UPDATE CASCADE,
    CONSTRAINT fk_incidents_acknowledged_by
        FOREIGN KEY (acknowledged_by) REFERENCES users(id)
        ON DELETE SET NULL ON UPDATE CASCADE
) ENGINE=InnoDB ROW_FORMAT=DYNAMIC COMMENT='Security incidents — grouped alerts confirmed as attacks';


CREATE TABLE alerts (
    id              CHAR(36)        NOT NULL,
    incident_id     CHAR(36)        DEFAULT NULL,
    endpoint_id     CHAR(36)        NOT NULL,
    agent_id        CHAR(36)        NOT NULL,
    alert_type      VARCHAR(100)    NOT NULL,
    severity        ENUM('info', 'warning', 'error', 'critical') NOT NULL,
    signal_source   ENUM('canary', 'entropy', 'process_tree', 'ml_anomaly', 'usb', 'exfil', 'signature') NOT NULL,
    signal_value    DECIMAL(10,4)   DEFAULT NULL,
    threshold       DECIMAL(10,4)   DEFAULT NULL,
    process_name    VARCHAR(255)    DEFAULT NULL,
    process_pid     INT UNSIGNED    DEFAULT NULL,
    file_path_hash  VARCHAR(64)     DEFAULT NULL,
    payload         JSON            DEFAULT NULL,
    created_at      TIMESTAMP(6)    NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    processed_at    TIMESTAMP(6)    DEFAULT NULL,
    PRIMARY KEY (id),
    KEY idx_alerts_incident (incident_id),
    KEY idx_alerts_endpoint_time (endpoint_id, created_at DESC),
    KEY idx_alerts_severity_time (severity, created_at DESC),
    KEY idx_alerts_unprocessed (processed_at, created_at),
    CONSTRAINT fk_alerts_incident
        FOREIGN KEY (incident_id) REFERENCES incidents(id)
        ON DELETE SET NULL ON UPDATE CASCADE,
    CONSTRAINT fk_alerts_endpoint
        FOREIGN KEY (endpoint_id) REFERENCES endpoints(id)
        ON DELETE CASCADE ON UPDATE CASCADE,
    CONSTRAINT fk_alerts_agent
        FOREIGN KEY (agent_id) REFERENCES agents(id)
        ON DELETE CASCADE ON UPDATE CASCADE
) ENGINE=InnoDB ROW_FORMAT=DYNAMIC COMMENT='Individual detection alerts — raw signals before correlation';


CREATE TABLE threat_signatures (
    id                  CHAR(36)        NOT NULL,
    signature_hash      VARCHAR(64)     NOT NULL,
    signature_type      ENUM('file_hash', 'process_name', 'registry', 'behavior', 'yara') NOT NULL,
    ransomware_family   VARCHAR(100)    DEFAULT NULL,
    severity            ENUM('low', 'medium', 'high', 'critical') NOT NULL,
    source              VARCHAR(100)    NOT NULL,
    description         TEXT            DEFAULT NULL,
    payload             LONGTEXT        NOT NULL,
    signature_ed25519   VARCHAR(128)    NOT NULL,
    cosignature_ed25519 VARCHAR(128)    DEFAULT NULL,
    published_at        TIMESTAMP(6)    NOT NULL,
    is_active           BOOLEAN         NOT NULL DEFAULT TRUE,
    created_at          TIMESTAMP(6)    NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    PRIMARY KEY (id),
    UNIQUE KEY uq_signatures_hash (signature_hash),
    KEY idx_signatures_type_active (signature_type, is_active),
    KEY idx_signatures_family (ransomware_family),
    KEY idx_signatures_published (published_at DESC)
) ENGINE=InnoDB ROW_FORMAT=DYNAMIC COMMENT='Threat intelligence signatures from cloud feed';


-- =============================================================================
-- DOMAIN 5: BACKUP
-- =============================================================================

CREATE TABLE backup_jobs (
    id                  CHAR(36)        NOT NULL,
    hospital_id         CHAR(36)        NOT NULL,
    job_type            ENUM('scheduled', 'emergency', 'manual') NOT NULL,
    started_at          TIMESTAMP(6)    NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    completed_at        TIMESTAMP(6)    DEFAULT NULL,
    status              ENUM('running', 'completed', 'failed', 'partial', 'cancelled') NOT NULL DEFAULT 'running',
    endpoints_count     INT UNSIGNED    NOT NULL DEFAULT 0,
    total_size_bytes    BIGINT UNSIGNED NOT NULL DEFAULT 0,
    encryption_algorithm VARCHAR(20)    NOT NULL DEFAULT 'AES-256-GCM',
    ironclad_used       BOOLEAN         NOT NULL DEFAULT FALSE,
    triggered_by_incident CHAR(36)      DEFAULT NULL,
    error_message       TEXT            DEFAULT NULL,
    PRIMARY KEY (id),
    KEY idx_backup_jobs_hospital_time (hospital_id, started_at DESC),
    KEY idx_backup_jobs_status (status, started_at DESC),
    KEY idx_backup_jobs_incident (triggered_by_incident),
    CONSTRAINT fk_backup_jobs_hospital
        FOREIGN KEY (hospital_id) REFERENCES hospitals(id)
        ON DELETE RESTRICT ON UPDATE CASCADE,
    CONSTRAINT fk_backup_jobs_incident
        FOREIGN KEY (triggered_by_incident) REFERENCES incidents(id)
        ON DELETE SET NULL ON UPDATE CASCADE
) ENGINE=InnoDB ROW_FORMAT=DYNAMIC COMMENT='Backup execution jobs — scheduled, emergency, or manual';


CREATE TABLE snapshots (
    id                      CHAR(36)        NOT NULL,
    backup_job_id           CHAR(36)        NOT NULL,
    endpoint_id             CHAR(36)        NOT NULL,
    snapshot_name           VARCHAR(255)    NOT NULL,
    created_at              TIMESTAMP(6)    NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    file_count              INT UNSIGNED    NOT NULL DEFAULT 0,
    size_bytes              BIGINT UNSIGNED NOT NULL DEFAULT 0,
    storage_location        VARCHAR(500)    NOT NULL,
    encryption_key_id       VARCHAR(64)     NOT NULL,
    integrity_hash          VARCHAR(64)     NOT NULL,
    verification_status     ENUM('pending', 'verified', 'corrupted') NOT NULL DEFAULT 'pending',
    verified_at             TIMESTAMP(6)    DEFAULT NULL,
    is_retained             BOOLEAN         NOT NULL DEFAULT TRUE,
    retention_until         TIMESTAMP(6)    DEFAULT NULL,
    PRIMARY KEY (id),
    KEY idx_snapshots_endpoint_time (endpoint_id, created_at DESC),
    KEY idx_snapshots_job (backup_job_id),
    KEY idx_snapshots_retained (is_retained, retention_until),
    KEY idx_snapshots_verification (verification_status),
    CONSTRAINT fk_snapshots_job
        FOREIGN KEY (backup_job_id) REFERENCES backup_jobs(id)
        ON DELETE RESTRICT ON UPDATE CASCADE,
    CONSTRAINT fk_snapshots_endpoint
        FOREIGN KEY (endpoint_id) REFERENCES endpoints(id)
        ON DELETE RESTRICT ON UPDATE CASCADE
) ENGINE=InnoDB ROW_FORMAT=DYNAMIC COMMENT='Individual snapshots — one per endpoint per backup job';


CREATE TABLE backup_files (
    id                          CHAR(36)        NOT NULL,
    snapshot_id                 CHAR(36)        NOT NULL,
    original_path_hash          VARCHAR(64)     NOT NULL,
    relative_path_encrypted     VARBINARY(1024) NOT NULL,
    size_bytes                  BIGINT UNSIGNED NOT NULL,
    content_hash                VARCHAR(64)     NOT NULL,
    modified_at                 TIMESTAMP(6)    NOT NULL,
    PRIMARY KEY (id),
    KEY idx_backup_files_snapshot (snapshot_id),
    KEY idx_backup_files_path_hash (original_path_hash),
    CONSTRAINT fk_backup_files_snapshot
        FOREIGN KEY (snapshot_id) REFERENCES snapshots(id)
        ON DELETE CASCADE ON UPDATE CASCADE
) ENGINE=InnoDB ROW_FORMAT=DYNAMIC COMMENT='Individual files within snapshots';


CREATE TABLE restore_requests (
    id                  CHAR(36)        NOT NULL,
    snapshot_id         CHAR(36)        NOT NULL,
    endpoint_id         CHAR(36)        NOT NULL,
    requested_by        CHAR(36)        NOT NULL,
    approved_by         CHAR(36)        DEFAULT NULL,
    request_scope       ENUM('full', 'selective') NOT NULL,
    files_filter        JSON            DEFAULT NULL,
    requested_at        TIMESTAMP(6)    NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    approved_at         TIMESTAMP(6)    DEFAULT NULL,
    executed_at         TIMESTAMP(6)    DEFAULT NULL,
    completed_at        TIMESTAMP(6)    DEFAULT NULL,
    status              ENUM('pending', 'approved', 'rejected', 'executing', 'completed', 'failed') NOT NULL DEFAULT 'pending',
    files_restored      INT UNSIGNED    NOT NULL DEFAULT 0,
    error_message       TEXT            DEFAULT NULL,
    PRIMARY KEY (id),
    KEY idx_restore_endpoint (endpoint_id, requested_at DESC),
    KEY idx_restore_status (status, requested_at DESC),
    CONSTRAINT fk_restore_snapshot
        FOREIGN KEY (snapshot_id) REFERENCES snapshots(id)
        ON DELETE RESTRICT ON UPDATE CASCADE,
    CONSTRAINT fk_restore_endpoint
        FOREIGN KEY (endpoint_id) REFERENCES endpoints(id)
        ON DELETE RESTRICT ON UPDATE CASCADE,
    CONSTRAINT fk_restore_requested_by
        FOREIGN KEY (requested_by) REFERENCES users(id)
        ON DELETE RESTRICT ON UPDATE CASCADE,
    CONSTRAINT fk_restore_approved_by
        FOREIGN KEY (approved_by) REFERENCES users(id)
        ON DELETE RESTRICT ON UPDATE CASCADE
) ENGINE=InnoDB ROW_FORMAT=DYNAMIC COMMENT='Restoration requests with double-authorization workflow';


-- =============================================================================
-- DOMAIN 6: LICENSING
-- =============================================================================

CREATE TABLE licenses (
    id                          CHAR(36)        NOT NULL,
    hospital_id                 CHAR(36)        NOT NULL,
    license_key                 VARCHAR(64)     NOT NULL,
    license_key_hash            VARCHAR(64)     NOT NULL,
    plan_type                   ENUM('starter', 'standard', 'enterprise') NOT NULL,
    max_endpoints               INT UNSIGNED    NOT NULL,
    issued_at                   TIMESTAMP(6)    NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    activated_at                TIMESTAMP(6)    DEFAULT NULL,
    expires_at                  TIMESTAMP(6)    NOT NULL,
    status                      ENUM('issued', 'active', 'grace', 'expired', 'revoked') NOT NULL DEFAULT 'issued',
    server_fingerprint          VARCHAR(64)     DEFAULT NULL,
    signed_activation_token     LONGTEXT        DEFAULT NULL,
    token_expires_at            TIMESTAMP(6)    DEFAULT NULL,
    last_validation_at          TIMESTAMP(6)    DEFAULT NULL,
    revocation_reason           VARCHAR(255)    DEFAULT NULL,
    created_at                  TIMESTAMP(6)    NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    updated_at                  TIMESTAMP(6)    NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
    version                     INT UNSIGNED    NOT NULL DEFAULT 1,
    PRIMARY KEY (id),
    UNIQUE KEY uq_licenses_key (license_key),
    UNIQUE KEY uq_licenses_key_hash (license_key_hash),
    KEY idx_licenses_hospital (hospital_id, status),
    KEY idx_licenses_expires (expires_at, status),
    CONSTRAINT fk_licenses_hospital
        FOREIGN KEY (hospital_id) REFERENCES hospitals(id)
        ON DELETE RESTRICT ON UPDATE CASCADE
) ENGINE=InnoDB ROW_FORMAT=DYNAMIC COMMENT='Annual licenses (Kaspersky-style) with offline grace period';


-- =============================================================================
-- DOMAIN 7: AUDIT (Hash-Chained Immutable Log)
-- =============================================================================

CREATE TABLE audit_logs (
    id                  BIGINT UNSIGNED AUTO_INCREMENT,
    sequence_number     BIGINT UNSIGNED NOT NULL,
    previous_hash       VARCHAR(64)     NOT NULL,
    current_hash        VARCHAR(64)     NOT NULL,
    event_type          VARCHAR(100)    NOT NULL,
    actor_type          ENUM('user', 'agent', 'system', 'cloud') NOT NULL,
    actor_id            CHAR(36)        DEFAULT NULL,
    actor_name          VARCHAR(255)    DEFAULT NULL,
    hospital_id         CHAR(36)        DEFAULT NULL,
    target_type         VARCHAR(50)     DEFAULT NULL,
    target_id           CHAR(36)        DEFAULT NULL,
    action              VARCHAR(100)    NOT NULL,
    payload             JSON            DEFAULT NULL,
    ip_address          VARCHAR(45)     DEFAULT NULL,
    user_agent          VARCHAR(500)    DEFAULT NULL,
    occurred_at         TIMESTAMP(6)    NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    PRIMARY KEY (id),
    UNIQUE KEY uq_audit_sequence (sequence_number),
    UNIQUE KEY uq_audit_current_hash (current_hash),
    KEY idx_audit_hospital_time (hospital_id, occurred_at DESC),
    KEY idx_audit_actor (actor_type, actor_id),
    KEY idx_audit_event_type (event_type, occurred_at DESC),
    KEY idx_audit_target (target_type, target_id)
) ENGINE=InnoDB ROW_FORMAT=DYNAMIC COMMENT='Append-only hash-chained audit log — SHA-256 integrity guarantee';


-- =============================================================================
-- DOMAIN 8: CONFIG
-- =============================================================================

CREATE TABLE whitelist_rules (
    id              CHAR(36)        NOT NULL,
    hospital_id     CHAR(36)        DEFAULT NULL,
    rule_type       ENUM('process_name', 'file_path', 'hash', 'publisher') NOT NULL,
    pattern         VARCHAR(500)    NOT NULL,
    is_global       BOOLEAN         NOT NULL DEFAULT FALSE,
    reason          TEXT            DEFAULT NULL,
    created_by      CHAR(36)        DEFAULT NULL,
    created_at      TIMESTAMP(6)    NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    is_active       BOOLEAN         NOT NULL DEFAULT TRUE,
    PRIMARY KEY (id),
    KEY idx_whitelist_hospital (hospital_id, is_active),
    KEY idx_whitelist_type_pattern (rule_type, pattern(255)),
    KEY idx_whitelist_global (is_global, is_active),
    CONSTRAINT fk_whitelist_hospital
        FOREIGN KEY (hospital_id) REFERENCES hospitals(id)
        ON DELETE CASCADE ON UPDATE CASCADE,
    CONSTRAINT fk_whitelist_created_by
        FOREIGN KEY (created_by) REFERENCES users(id)
        ON DELETE SET NULL ON UPDATE CASCADE
) ENGINE=InnoDB ROW_FORMAT=DYNAMIC COMMENT='Whitelist rules — hospital-specific or global defaults';


CREATE TABLE critical_processes (
    id                  CHAR(36)        NOT NULL,
    hospital_id         CHAR(36)        DEFAULT NULL,
    process_name        VARCHAR(255)    NOT NULL,
    process_hash        VARCHAR(64)     DEFAULT NULL,
    publisher           VARCHAR(255)    DEFAULT NULL,
    category            ENUM('hms', 'laboratory', 'imaging', 'pharmacy', 'generic_medical', 'system') NOT NULL,
    is_global_default   BOOLEAN         NOT NULL DEFAULT FALSE,
    never_isolate       BOOLEAN         NOT NULL DEFAULT TRUE,
    description         TEXT            DEFAULT NULL,
    created_at          TIMESTAMP(6)    NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    PRIMARY KEY (id),
    KEY idx_critical_hospital (hospital_id),
    KEY idx_critical_global (is_global_default),
    KEY idx_critical_process_name (process_name),
    CONSTRAINT fk_critical_hospital
        FOREIGN KEY (hospital_id) REFERENCES hospitals(id)
        ON DELETE CASCADE ON UPDATE CASCADE
) ENGINE=InnoDB ROW_FORMAT=DYNAMIC COMMENT='Medical-critical processes — never auto-isolate';


-- =============================================================================
-- INITIAL DATA — Global Critical Processes (Cameroon Hospitals)
-- =============================================================================

INSERT INTO critical_processes (id, process_name, category, is_global_default, never_isolate, description) VALUES
(UUID(), 'mediboard.exe',          'hms',              TRUE, TRUE, 'MediBoard HMS — used at Yaoundé Central Hospital'),
(UUID(), 'sage_medical.exe',       'hms',              TRUE, TRUE, 'Sage Medical software'),
(UUID(), 'odoo.exe',               'hms',              TRUE, TRUE, 'Odoo HMS module'),
(UUID(), 'lis_lab.exe',            'laboratory',       TRUE, TRUE, 'Laboratory Information System generic'),
(UUID(), 'mindray_workstation.exe','laboratory',       TRUE, TRUE, 'Mindray laboratory analyzer interface'),
(UUID(), 'ge_centricity.exe',      'imaging',          TRUE, TRUE, 'GE Centricity imaging'),
(UUID(), 'philips_dicom.exe',      'imaging',          TRUE, TRUE, 'Philips DICOM viewer'),
(UUID(), 'pharma_manager.exe',     'pharmacy',         TRUE, TRUE, 'Generic pharmacy management'),
(UUID(), 'svchost.exe',            'system',           TRUE, TRUE, 'Windows critical system process'),
(UUID(), 'lsass.exe',              'system',           TRUE, TRUE, 'Windows Local Security Authority'),
(UUID(), 'winlogon.exe',           'system',           TRUE, TRUE, 'Windows Logon process'),
(UUID(), 'csrss.exe',              'system',           TRUE, TRUE, 'Windows Client Server Runtime');


-- =============================================================================
-- VIEWS — For Dashboard Performance
-- =============================================================================

CREATE OR REPLACE VIEW v_active_incidents AS
SELECT
    i.id,
    i.incident_number,
    i.hospital_id,
    h.name AS hospital_name,
    i.endpoint_id,
    e.hostname AS endpoint_hostname,
    e.department,
    i.severity,
    i.category,
    i.title,
    i.detected_at,
    i.contained_at,
    i.status,
    i.signal_count,
    TIMESTAMPDIFF(SECOND, i.detected_at, i.contained_at) AS containment_seconds
FROM incidents i
INNER JOIN hospitals h ON h.id = i.hospital_id
INNER JOIN endpoints e ON e.id = i.endpoint_id
WHERE i.status IN ('detected', 'contained', 'investigating')
  AND h.deleted_at IS NULL
  AND e.deleted_at IS NULL;


CREATE OR REPLACE VIEW v_endpoint_health AS
SELECT
    e.id,
    e.hospital_id,
    e.hostname,
    e.department,
    e.criticality,
    e.is_medical_critical,
    e.status,
    e.last_seen_at,
    a.version AS agent_version,
    a.health_status,
    a.last_heartbeat_at,
    TIMESTAMPDIFF(SECOND, a.last_heartbeat_at, NOW(6)) AS seconds_since_heartbeat,
    CASE
        WHEN a.last_heartbeat_at IS NULL THEN 'never_seen'
        WHEN TIMESTAMPDIFF(MINUTE, a.last_heartbeat_at, NOW(6)) < 2 THEN 'online'
        WHEN TIMESTAMPDIFF(MINUTE, a.last_heartbeat_at, NOW(6)) < 15 THEN 'degraded'
        ELSE 'offline'
    END AS connectivity_status
FROM endpoints e
LEFT JOIN agents a ON a.endpoint_id = e.id
WHERE e.deleted_at IS NULL;


-- =============================================================================
-- STORED PROCEDURE — Audit Log Append with Hash Chain
-- =============================================================================

DELIMITER //

CREATE PROCEDURE sp_append_audit_log(
    IN  p_event_type    VARCHAR(100),
    IN  p_actor_type    VARCHAR(20),
    IN  p_actor_id      CHAR(36),
    IN  p_hospital_id   CHAR(36),
    IN  p_target_type   VARCHAR(50),
    IN  p_target_id     CHAR(36),
    IN  p_action        VARCHAR(100),
    IN  p_payload       JSON,
    IN  p_ip_address    VARCHAR(45)
)
BEGIN
    DECLARE v_last_hash         VARCHAR(64) DEFAULT REPEAT('0', 64);
    DECLARE v_sequence_number   BIGINT UNSIGNED;
    DECLARE v_new_hash          VARCHAR(64);
    DECLARE v_occurred_at       TIMESTAMP(6);

    SET v_occurred_at = CURRENT_TIMESTAMP(6);

    -- Get previous hash and next sequence number atomically
    SELECT current_hash, sequence_number + 1
    INTO v_last_hash, v_sequence_number
    FROM audit_logs
    ORDER BY sequence_number DESC
    LIMIT 1;

    -- First entry case
    IF v_sequence_number IS NULL THEN
        SET v_sequence_number = 1;
    END IF;

    -- Compute hash of current entry (chained with previous)
    SET v_new_hash = SHA2(CONCAT_WS('|',
        v_sequence_number,
        v_last_hash,
        p_event_type,
        IFNULL(p_actor_type, ''),
        IFNULL(p_actor_id, ''),
        IFNULL(p_hospital_id, ''),
        IFNULL(p_target_type, ''),
        IFNULL(p_target_id, ''),
        p_action,
        IFNULL(CAST(p_payload AS CHAR), ''),
        IFNULL(p_ip_address, ''),
        v_occurred_at
    ), 256);

    INSERT INTO audit_logs (
        sequence_number, previous_hash, current_hash,
        event_type, actor_type, actor_id, hospital_id,
        target_type, target_id, action,
        payload, ip_address, occurred_at
    ) VALUES (
        v_sequence_number, v_last_hash, v_new_hash,
        p_event_type, p_actor_type, p_actor_id, p_hospital_id,
        p_target_type, p_target_id, p_action,
        p_payload, p_ip_address, v_occurred_at
    );
END //

DELIMITER ;


-- =============================================================================
-- TRIGGERS — Automatic version increment on update
-- =============================================================================

DELIMITER //

CREATE TRIGGER tr_hospitals_version_bump
BEFORE UPDATE ON hospitals
FOR EACH ROW
BEGIN
    SET NEW.version = OLD.version + 1;
END //

CREATE TRIGGER tr_users_version_bump
BEFORE UPDATE ON users
FOR EACH ROW
BEGIN
    SET NEW.version = OLD.version + 1;
END //

CREATE TRIGGER tr_endpoints_version_bump
BEFORE UPDATE ON endpoints
FOR EACH ROW
BEGIN
    SET NEW.version = OLD.version + 1;
END //

CREATE TRIGGER tr_incidents_version_bump
BEFORE UPDATE ON incidents
FOR EACH ROW
BEGIN
    SET NEW.version = OLD.version + 1;
END //

CREATE TRIGGER tr_licenses_version_bump
BEFORE UPDATE ON licenses
FOR EACH ROW
BEGIN
    SET NEW.version = OLD.version + 1;
END //

DELIMITER ;


-- =============================================================================
-- DATABASE STATISTICS — Display Schema Summary
-- =============================================================================

SELECT
    TABLE_NAME,
    TABLE_COMMENT,
    ROUND(((DATA_LENGTH + INDEX_LENGTH) / 1024), 2) AS size_kb
FROM information_schema.TABLES
WHERE TABLE_SCHEMA = 'ransomguard_cm'
ORDER BY TABLE_NAME;

-- End of schema
