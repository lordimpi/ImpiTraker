IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'ux_plans_code' AND object_id = OBJECT_ID('plans'))
BEGIN
    CREATE UNIQUE INDEX ux_plans_code ON plans(code);
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'ix_user_plan_subscriptions_active' AND object_id = OBJECT_ID('user_plan_subscriptions'))
BEGIN
    CREATE INDEX ix_user_plan_subscriptions_active
        ON user_plan_subscriptions(user_id, status, ends_at_utc);
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'ix_user_devices_user_active' AND object_id = OBJECT_ID('user_devices'))
BEGIN
    CREATE INDEX ix_user_devices_user_active
        ON user_devices(user_id, is_active, imei);
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'ux_user_devices_imei_active' AND object_id = OBJECT_ID('user_devices'))
BEGIN
    CREATE UNIQUE INDEX ux_user_devices_imei_active
        ON user_devices(imei)
        WHERE is_active = 1;
END;

