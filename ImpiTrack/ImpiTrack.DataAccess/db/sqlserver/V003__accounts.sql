IF OBJECT_ID('plans', 'U') IS NULL
BEGIN
    CREATE TABLE plans
    (
        plan_id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
        code NVARCHAR(32) NOT NULL,
        name NVARCHAR(64) NOT NULL,
        max_gps INT NOT NULL,
        is_active BIT NOT NULL,
        created_at_utc DATETIMEOFFSET NOT NULL,
        updated_at_utc DATETIMEOFFSET NOT NULL
    );
END;

IF OBJECT_ID('user_profiles', 'U') IS NULL
BEGIN
    CREATE TABLE user_profiles
    (
        user_id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
        email NVARCHAR(256) NOT NULL,
        full_name NVARCHAR(128) NULL,
        email_verified_at_utc DATETIMEOFFSET NULL,
        created_at_utc DATETIMEOFFSET NOT NULL,
        updated_at_utc DATETIMEOFFSET NOT NULL
    );
END;

IF OBJECT_ID('user_plan_subscriptions', 'U') IS NULL
BEGIN
    CREATE TABLE user_plan_subscriptions
    (
        subscription_id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
        user_id UNIQUEIDENTIFIER NOT NULL,
        plan_id UNIQUEIDENTIFIER NOT NULL,
        status NVARCHAR(16) NOT NULL,
        starts_at_utc DATETIMEOFFSET NOT NULL,
        ends_at_utc DATETIMEOFFSET NULL,
        created_at_utc DATETIMEOFFSET NOT NULL,
        CONSTRAINT fk_user_plan_subscriptions_profiles
            FOREIGN KEY (user_id) REFERENCES user_profiles(user_id),
        CONSTRAINT fk_user_plan_subscriptions_plans
            FOREIGN KEY (plan_id) REFERENCES plans(plan_id)
    );
END;

IF OBJECT_ID('user_devices', 'U') IS NULL
BEGIN
    CREATE TABLE user_devices
    (
        user_device_id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
        user_id UNIQUEIDENTIFIER NOT NULL,
        device_id UNIQUEIDENTIFIER NOT NULL,
        imei NVARCHAR(32) NOT NULL,
        bound_at_utc DATETIMEOFFSET NOT NULL,
        unbound_at_utc DATETIMEOFFSET NULL,
        is_active BIT NOT NULL,
        CONSTRAINT fk_user_devices_profiles
            FOREIGN KEY (user_id) REFERENCES user_profiles(user_id),
        CONSTRAINT fk_user_devices_devices
            FOREIGN KEY (device_id) REFERENCES devices(device_id)
    );
END;

IF NOT EXISTS (SELECT 1 FROM plans WHERE code = 'BASIC')
BEGIN
    INSERT INTO plans(plan_id, code, name, max_gps, is_active, created_at_utc, updated_at_utc)
    VALUES (NEWID(), 'BASIC', 'Basic', 3, 1, SYSUTCDATETIME(), SYSUTCDATETIME());
END;

IF NOT EXISTS (SELECT 1 FROM plans WHERE code = 'PRO')
BEGIN
    INSERT INTO plans(plan_id, code, name, max_gps, is_active, created_at_utc, updated_at_utc)
    VALUES (NEWID(), 'PRO', 'Pro', 10, 1, SYSUTCDATETIME(), SYSUTCDATETIME());
END;

IF NOT EXISTS (SELECT 1 FROM plans WHERE code = 'ENTERPRISE')
BEGIN
    INSERT INTO plans(plan_id, code, name, max_gps, is_active, created_at_utc, updated_at_utc)
    VALUES (NEWID(), 'ENTERPRISE', 'Enterprise', 100, 1, SYSUTCDATETIME(), SYSUTCDATETIME());
END;

