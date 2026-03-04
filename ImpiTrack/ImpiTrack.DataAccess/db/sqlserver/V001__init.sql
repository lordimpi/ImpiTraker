CREATE TABLE devices (
    device_id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    imei NVARCHAR(32) NOT NULL,
    created_at_utc DATETIMEOFFSET NOT NULL,
    last_seen_at_utc DATETIMEOFFSET NOT NULL
);

CREATE UNIQUE INDEX ux_devices_imei ON devices(imei);

CREATE TABLE device_sessions (
    session_id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    device_id UNIQUEIDENTIFIER NULL,
    imei NVARCHAR(32) NULL,
    remote_ip NVARCHAR(64) NOT NULL,
    port INT NOT NULL,
    connected_at_utc DATETIMEOFFSET NOT NULL,
    last_seen_at_utc DATETIMEOFFSET NOT NULL,
    last_heartbeat_at_utc DATETIMEOFFSET NULL,
    frames_in BIGINT NOT NULL,
    frames_invalid BIGINT NOT NULL,
    close_reason NVARCHAR(64) NULL,
    disconnected_at_utc DATETIMEOFFSET NULL,
    is_active BIT NOT NULL,
    CONSTRAINT fk_device_sessions_devices
        FOREIGN KEY (device_id)
        REFERENCES devices(device_id)
);

CREATE TABLE raw_packets (
    packet_id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    session_id UNIQUEIDENTIFIER NOT NULL,
    device_id UNIQUEIDENTIFIER NULL,
    imei NVARCHAR(32) NULL,
    port INT NOT NULL,
    remote_ip NVARCHAR(64) NOT NULL,
    protocol INT NOT NULL,
    message_type INT NOT NULL,
    payload_text NVARCHAR(MAX) NOT NULL,
    received_at_utc DATETIMEOFFSET NOT NULL,
    parse_status INT NOT NULL,
    parse_error NVARCHAR(128) NULL,
    ack_sent BIT NOT NULL,
    ack_payload NVARCHAR(256) NULL,
    ack_at_utc DATETIMEOFFSET NULL,
    ack_latency_ms FLOAT NULL,
    queue_backlog BIGINT NOT NULL,
    CONSTRAINT fk_raw_packets_device_sessions
        FOREIGN KEY (session_id)
        REFERENCES device_sessions(session_id),
    CONSTRAINT fk_raw_packets_devices
        FOREIGN KEY (device_id)
        REFERENCES devices(device_id)
);

CREATE TABLE positions (
    position_id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    packet_id UNIQUEIDENTIFIER NOT NULL,
    session_id UNIQUEIDENTIFIER NOT NULL,
    device_id UNIQUEIDENTIFIER NULL,
    imei NVARCHAR(32) NULL,
    protocol INT NOT NULL,
    message_type INT NOT NULL,
    gps_time_utc DATETIMEOFFSET NOT NULL,
    latitude DECIMAL(10, 7) NULL,
    longitude DECIMAL(10, 7) NULL,
    speed_kmh FLOAT NULL,
    heading_deg INT NULL,
    created_at_utc DATETIMEOFFSET NOT NULL,
    CONSTRAINT fk_positions_packets
        FOREIGN KEY (packet_id)
        REFERENCES raw_packets(packet_id),
    CONSTRAINT fk_positions_sessions
        FOREIGN KEY (session_id)
        REFERENCES device_sessions(session_id),
    CONSTRAINT fk_positions_devices
        FOREIGN KEY (device_id)
        REFERENCES devices(device_id)
);

CREATE TABLE device_events (
    event_id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    packet_id UNIQUEIDENTIFIER NOT NULL,
    session_id UNIQUEIDENTIFIER NOT NULL,
    device_id UNIQUEIDENTIFIER NULL,
    imei NVARCHAR(32) NULL,
    protocol INT NOT NULL,
    message_type INT NOT NULL,
    event_code NVARCHAR(64) NOT NULL,
    payload_text NVARCHAR(MAX) NOT NULL,
    received_at_utc DATETIMEOFFSET NOT NULL,
    CONSTRAINT fk_device_events_packets
        FOREIGN KEY (packet_id)
        REFERENCES raw_packets(packet_id),
    CONSTRAINT fk_device_events_sessions
        FOREIGN KEY (session_id)
        REFERENCES device_sessions(session_id),
    CONSTRAINT fk_device_events_devices
        FOREIGN KEY (device_id)
        REFERENCES devices(device_id)
);

CREATE TABLE port_ingestion_snapshots (
    port INT NOT NULL PRIMARY KEY,
    frames_in BIGINT NOT NULL,
    parse_ok BIGINT NOT NULL,
    parse_fail BIGINT NOT NULL,
    ack_sent BIGINT NOT NULL,
    backlog BIGINT NOT NULL,
    last_received_at_utc DATETIMEOFFSET NOT NULL
);
