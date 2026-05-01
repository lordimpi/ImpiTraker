-- V009__device_commands.sql
-- Adds device_commands audit/persistence table and protocol column to user_devices.
-- Added 2026-05-01 — gps-device-commands change.
--
-- DECISION (AD-8 + explicit user preference): protocol VARCHAR(16) is added to
-- user_devices here (rather than a separate V010 follow-up) to avoid the edge case
-- where a device with no position history cannot receive commands because protocol
-- resolution falls through all three resolution paths (session.Protocol →
-- user_devices.protocol → positions.protocol) without finding a value.
-- Populating this column on first authenticated session removes that dependency.

ALTER TABLE user_devices
    ADD COLUMN IF NOT EXISTS protocol VARCHAR(16) NULL;

CREATE TABLE IF NOT EXISTS device_commands (
    command_id            UUID          PRIMARY KEY,
    imei                  VARCHAR(32)   NOT NULL,
    user_id               UUID          NOT NULL,
    command_type          VARCHAR(64)   NOT NULL,
    parameters            JSONB,
    protocol              VARCHAR(16),
    payload_sent          TEXT,
    correlation_key       VARCHAR(64),
    correlation_timestamp VARCHAR(8),
    status                VARCHAR(32)   NOT NULL,
    queued_at_utc         TIMESTAMPTZ   NOT NULL,
    sent_at_utc           TIMESTAMPTZ,
    acked_at_utc          TIMESTAMPTZ,
    response_code         VARCHAR(32),
    response_text         TEXT,
    failure_reason        TEXT,
    user_ip               VARCHAR(64),
    user_agent            VARCHAR(255)
);

CREATE INDEX IF NOT EXISTS ix_device_commands_imei_status
    ON device_commands(imei, status);

CREATE INDEX IF NOT EXISTS ix_device_commands_user_id
    ON device_commands(user_id, queued_at_utc DESC);

CREATE INDEX IF NOT EXISTS ix_device_commands_status_sent_at
    ON device_commands(status, sent_at_utc)
    WHERE status = 'Sent';

-- Cantrack exact-match correlation lookup: filter Sent + (imei, key, timestamp).
-- Spec REQ-DB-1: include correlation_timestamp in the Sent-filtered correlation index.
CREATE INDEX IF NOT EXISTS ix_device_commands_imei_correlation
    ON device_commands(imei, correlation_key, correlation_timestamp)
    WHERE status = 'Sent';

-- Reconnect-recovery hot path: oldest-Queued-by-IMEI lookup.
-- Spec design section 11: ix_device_commands_imei_queued.
CREATE INDEX IF NOT EXISTS ix_device_commands_imei_queued
    ON device_commands(imei, queued_at_utc)
    WHERE status = 'Queued';
