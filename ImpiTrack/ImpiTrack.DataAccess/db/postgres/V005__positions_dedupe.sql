ALTER TABLE positions
    ADD COLUMN IF NOT EXISTS dedupe_key VARCHAR(128) NULL;

CREATE UNIQUE INDEX IF NOT EXISTS ux_positions_dedupe_key
    ON positions(dedupe_key)
    WHERE dedupe_key IS NOT NULL;
