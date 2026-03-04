CREATE TABLE devices (
    device_id UUID PRIMARY KEY,
    imei VARCHAR(32) NOT NULL UNIQUE,
    created_at_utc TIMESTAMPTZ NOT NULL,
    last_seen_at_utc TIMESTAMPTZ NOT NULL
);

CREATE TABLE device_sessions (
    session_id UUID PRIMARY KEY,
    device_id UUID NULL REFERENCES devices(device_id),
    imei VARCHAR(32) NULL,
    remote_ip VARCHAR(64) NOT NULL,
    port INTEGER NOT NULL,
    connected_at_utc TIMESTAMPTZ NOT NULL,
    last_seen_at_utc TIMESTAMPTZ NOT NULL,
    last_heartbeat_at_utc TIMESTAMPTZ NULL,
    frames_in BIGINT NOT NULL,
    frames_invalid BIGINT NOT NULL,
    close_reason VARCHAR(64) NULL,
    disconnected_at_utc TIMESTAMPTZ NULL,
    is_active BOOLEAN NOT NULL
);

CREATE TABLE raw_packets (
    packet_id UUID PRIMARY KEY,
    session_id UUID NOT NULL REFERENCES device_sessions(session_id),
    device_id UUID NULL REFERENCES devices(device_id),
    imei VARCHAR(32) NULL,
    port INTEGER NOT NULL,
    remote_ip VARCHAR(64) NOT NULL,
    protocol INTEGER NOT NULL,
    message_type INTEGER NOT NULL,
    payload_text TEXT NOT NULL,
    received_at_utc TIMESTAMPTZ NOT NULL,
    parse_status INTEGER NOT NULL,
    parse_error VARCHAR(128) NULL,
    ack_sent BOOLEAN NOT NULL,
    ack_payload VARCHAR(256) NULL,
    ack_at_utc TIMESTAMPTZ NULL,
    ack_latency_ms DOUBLE PRECISION NULL,
    queue_backlog BIGINT NOT NULL
);

CREATE TABLE positions (
    position_id UUID PRIMARY KEY,
    packet_id UUID NOT NULL REFERENCES raw_packets(packet_id),
    session_id UUID NOT NULL REFERENCES device_sessions(session_id),
    device_id UUID NULL REFERENCES devices(device_id),
    imei VARCHAR(32) NULL,
    protocol INTEGER NOT NULL,
    message_type INTEGER NOT NULL,
    gps_time_utc TIMESTAMPTZ NOT NULL,
    latitude NUMERIC(10, 7) NULL,
    longitude NUMERIC(10, 7) NULL,
    speed_kmh DOUBLE PRECISION NULL,
    heading_deg INTEGER NULL,
    created_at_utc TIMESTAMPTZ NOT NULL
);

CREATE TABLE device_events (
    event_id UUID PRIMARY KEY,
    packet_id UUID NOT NULL REFERENCES raw_packets(packet_id),
    session_id UUID NOT NULL REFERENCES device_sessions(session_id),
    device_id UUID NULL REFERENCES devices(device_id),
    imei VARCHAR(32) NULL,
    protocol INTEGER NOT NULL,
    message_type INTEGER NOT NULL,
    event_code VARCHAR(64) NOT NULL,
    payload_text TEXT NOT NULL,
    received_at_utc TIMESTAMPTZ NOT NULL
);

CREATE TABLE port_ingestion_snapshots (
    port INTEGER PRIMARY KEY,
    frames_in BIGINT NOT NULL,
    parse_ok BIGINT NOT NULL,
    parse_fail BIGINT NOT NULL,
    ack_sent BIGINT NOT NULL,
    backlog BIGINT NOT NULL,
    last_received_at_utc TIMESTAMPTZ NOT NULL
);
