IF NOT EXISTS (
    SELECT 1
    FROM sys.columns
    WHERE object_id = OBJECT_ID('user_devices')
      AND name = 'alias'
)
BEGIN
    ALTER TABLE user_devices
        ADD alias NVARCHAR(50) NULL;
END;
