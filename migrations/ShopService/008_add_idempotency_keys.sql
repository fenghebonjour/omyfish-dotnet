-- Idempotency cache for /checkout. A client-supplied Idempotency-Key reserves a row before
-- ReserveOrderCommand and the payment-processor call run; the row is then completed with the
-- resulting order/checkout-url so a retried request with the same key replays it instead of
-- reserving stock and creating an order a second time. The unique constraint also catches two
-- concurrent requests racing on the same key. Same shape as omyfish-java's
-- shop.idempotency_keys (V9__add_idempotency_keys.sql).
CREATE TABLE IF NOT EXISTS shop.idempotency_keys (
    id              UUID PRIMARY KEY,
    idempotency_key VARCHAR(255) NOT NULL,
    endpoint        VARCHAR(20) NOT NULL,
    owner_key       VARCHAR(255) NOT NULL,
    completed       BOOLEAN NOT NULL DEFAULT FALSE,
    order_id        UUID,
    checkout_url    VARCHAR(512),
    created_at      TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    CONSTRAINT idempotency_keys_key_endpoint_unique UNIQUE (idempotency_key, endpoint)
);
