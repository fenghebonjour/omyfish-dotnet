-- Idempotency cache for payment-mutating endpoints (checkout, refund). A
-- client-supplied Idempotency-Key reserves a row before the Stripe call
-- runs; the row is then completed with the result so a retried request
-- with the same key replays it instead of calling Stripe again. The unique
-- constraint also catches two concurrent requests racing on the same key.
CREATE TABLE IF NOT EXISTS idempotency_keys (
    id                  UUID PRIMARY KEY,
    idempotency_key     VARCHAR(255) NOT NULL,
    endpoint            VARCHAR(20) NOT NULL,
    user_id             UUID NOT NULL,
    completed           BOOLEAN NOT NULL DEFAULT FALSE,
    customer_id         VARCHAR(255),
    subscription_id     VARCHAR(255),
    client_secret       VARCHAR(255),
    checkout_status     VARCHAR(50),
    refund_id           VARCHAR(255),
    refund_status       VARCHAR(50),
    amount_cents        BIGINT,
    created_at          TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT idempotency_keys_key_endpoint_unique UNIQUE (idempotency_key, endpoint)
);
