CREATE UNIQUE INDEX IF NOT EXISTS ux_plans_code ON plans(code);

CREATE INDEX IF NOT EXISTS ix_user_plan_subscriptions_active
    ON user_plan_subscriptions(user_id, status, ends_at_utc);

CREATE INDEX IF NOT EXISTS ix_user_devices_user_active
    ON user_devices(user_id, is_active, imei);

CREATE UNIQUE INDEX IF NOT EXISTS ux_user_devices_imei_active
    ON user_devices(imei)
    WHERE is_active = TRUE;

