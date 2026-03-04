IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'ux_positions_dedupe_key' AND object_id = OBJECT_ID('positions'))
BEGIN
    CREATE UNIQUE INDEX ux_positions_dedupe_key
        ON positions(dedupe_key)
        WHERE dedupe_key IS NOT NULL;
END;
