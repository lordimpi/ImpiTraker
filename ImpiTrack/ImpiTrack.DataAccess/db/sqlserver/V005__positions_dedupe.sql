IF COL_LENGTH('positions', 'dedupe_key') IS NULL
BEGIN
    ALTER TABLE positions
        ADD dedupe_key NVARCHAR(128) NULL;
END;
