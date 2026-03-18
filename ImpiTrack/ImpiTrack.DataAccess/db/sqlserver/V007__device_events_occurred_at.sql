IF NOT EXISTS (
    SELECT 1
    FROM sys.columns
    WHERE object_id = OBJECT_ID('device_events')
      AND name = 'occurred_at_utc'
)
BEGIN
    ALTER TABLE device_events
        ADD occurred_at_utc DATETIMEOFFSET NULL;
END;
