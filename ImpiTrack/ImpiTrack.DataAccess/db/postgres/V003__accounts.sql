CREATE TABLE IF NOT EXISTS plans
(
    plan_id UUID PRIMARY KEY,
    code VARCHAR(32) NOT NULL UNIQUE,
    name VARCHAR(64) NOT NULL,
    max_gps INTEGER NOT NULL,
    is_active BOOLEAN NOT NULL,
    created_at_utc TIMESTAMPTZ NOT NULL,
    updated_at_utc TIMESTAMPTZ NOT NULL
);

CREATE TABLE IF NOT EXISTS user_profiles
(
    user_id UUID PRIMARY KEY,
    email VARCHAR(256) NOT NULL,
    full_name VARCHAR(128) NULL,
    email_verified_at_utc TIMESTAMPTZ NULL,
    created_at_utc TIMESTAMPTZ NOT NULL,
    updated_at_utc TIMESTAMPTZ NOT NULL
);

CREATE TABLE IF NOT EXISTS user_plan_subscriptions
(
    subscription_id UUID PRIMARY KEY,
    user_id UUID NOT NULL REFERENCES user_profiles(user_id),
    plan_id UUID NOT NULL REFERENCES plans(plan_id),
    status VARCHAR(16) NOT NULL,
    starts_at_utc TIMESTAMPTZ NOT NULL,
    ends_at_utc TIMESTAMPTZ NULL,
    created_at_utc TIMESTAMPTZ NOT NULL
);

CREATE TABLE IF NOT EXISTS user_devices
(
    user_device_id UUID PRIMARY KEY,
    user_id UUID NOT NULL REFERENCES user_profiles(user_id),
    device_id UUID NOT NULL REFERENCES devices(device_id),
    imei VARCHAR(32) NOT NULL,
    bound_at_utc TIMESTAMPTZ NOT NULL,
    unbound_at_utc TIMESTAMPTZ NULL,
    is_active BOOLEAN NOT NULL
);

INSERT INTO plans(plan_id, code, name, max_gps, is_active, created_at_utc, updated_at_utc)
VALUES ('00000000-0000-0000-0000-000000000101', 'BASIC', 'Basic', 3, TRUE, NOW(), NOW())
ON CONFLICT (code) DO NOTHING;

INSERT INTO plans(plan_id, code, name, max_gps, is_active, created_at_utc, updated_at_utc)
VALUES ('00000000-0000-0000-0000-000000000102', 'PRO', 'Pro', 10, TRUE, NOW(), NOW())
ON CONFLICT (code) DO NOTHING;

INSERT INTO plans(plan_id, code, name, max_gps, is_active, created_at_utc, updated_at_utc)
VALUES ('00000000-0000-0000-0000-000000000103', 'ENTERPRISE', 'Enterprise', 100, TRUE, NOW(), NOW())
ON CONFLICT (code) DO NOTHING;
