-- Subscriptions: 7-day trial on registration, then Stripe-managed
-- monthly (5 CAD) or yearly (29 CAD) plans.
CREATE TABLE IF NOT EXISTS subscriptions (
    id                      UUID PRIMARY KEY,
    user_id                 UUID NOT NULL UNIQUE REFERENCES users(id) ON DELETE CASCADE,
    status                  VARCHAR(20) NOT NULL DEFAULT 'trialing',
    plan                    VARCHAR(20),
    trial_end               TIMESTAMPTZ,
    current_period_end      TIMESTAMPTZ,
    stripe_customer_id      VARCHAR(255),
    stripe_subscription_id  VARCHAR(255),
    created_at              TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at              TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX IF NOT EXISTS subscriptions_stripe_customer_idx
    ON subscriptions (stripe_customer_id);
