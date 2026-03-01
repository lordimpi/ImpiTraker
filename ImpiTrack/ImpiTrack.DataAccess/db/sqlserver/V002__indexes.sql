CREATE INDEX ix_device_sessions_active_port ON device_sessions(is_active, port, last_seen_at_utc);
CREATE INDEX ix_raw_packets_imei_received ON raw_packets(imei, received_at_utc);
CREATE INDEX ix_raw_packets_received ON raw_packets(received_at_utc);
CREATE INDEX ix_positions_imei_gps ON positions(imei, gps_time_utc);
CREATE INDEX ix_device_events_imei_received ON device_events(imei, received_at_utc);
