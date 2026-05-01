-- V009__device_commands.sql
-- Adds device_commands audit/persistence table and protocol column to user_devices.
-- Added 2026-05-01 — gps-device-commands change.
--
-- DECISION (AD-8 + explicit user preference): protocol column added to user_devices
-- here to avoid devices with no position history failing protocol resolution.

IF NOT EXISTS (
    SELECT 1
    FROM sys.columns
    WHERE object_id = OBJECT_ID('user_devices')
      AND name = 'protocol'
)
BEGIN
    ALTER TABLE user_devices
        ADD protocol NVARCHAR(16) NULL;
END;

IF NOT EXISTS (SELECT 1 FROM sys.objects WHERE object_id = OBJECT_ID(N'device_commands') AND type = N'U')
BEGIN
    CREATE TABLE device_commands (
        command_id            UNIQUEIDENTIFIER  NOT NULL PRIMARY KEY,
        imei                  NVARCHAR(32)      NOT NULL,
        user_id               UNIQUEIDENTIFIER  NOT NULL,
        command_type          NVARCHAR(64)      NOT NULL,
        parameters            NVARCHAR(MAX)     NULL,
        protocol              NVARCHAR(16)      NULL,
        payload_sent          NVARCHAR(MAX)     NULL,
        correlation_key       NVARCHAR(64)      NULL,
        correlation_timestamp NVARCHAR(8)       NULL,
        status                NVARCHAR(32)      NOT NULL,
        queued_at_utc         DATETIMEOFFSET    NOT NULL,
        sent_at_utc           DATETIMEOFFSET    NULL,
        acked_at_utc          DATETIMEOFFSET    NULL,
        response_code         NVARCHAR(32)      NULL,
        response_text         NVARCHAR(MAX)     NULL,
        failure_reason        NVARCHAR(MAX)     NULL,
        user_ip               NVARCHAR(64)      NULL,
        user_agent            NVARCHAR(255)     NULL
    );

    CREATE INDEX ix_device_commands_imei_status
        ON device_commands(imei, status);

    CREATE INDEX ix_device_commands_user_id
        ON device_commands(user_id, queued_at_utc DESC);

    CREATE INDEX ix_device_commands_status_sent_at
        ON device_commands(status, sent_at_utc)
        WHERE status = 'Sent';

    -- Cantrack exact-match correlation lookup: filter Sent + (imei, key, timestamp).
    -- Spec REQ-DB-1: include correlation_timestamp in the Sent-filtered correlation index.
    CREATE INDEX ix_device_commands_imei_correlation
        ON device_commands(imei, correlation_key, correlation_timestamp)
        WHERE status = 'Sent';

    -- Reconnect-recovery hot path: oldest-Queued-by-IMEI lookup.
    -- Spec design section 11: ix_device_commands_imei_queued.
    CREATE INDEX ix_device_commands_imei_queued
        ON device_commands(imei, queued_at_utc)
        WHERE status = 'Queued';
END;
