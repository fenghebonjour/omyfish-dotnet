CREATE TABLE shop.orders (
    id                    UUID PRIMARY KEY,
    user_id               UUID NOT NULL,
    total_cents           BIGINT NOT NULL,
    status                VARCHAR(32) NOT NULL,
    stripe_session_id     VARCHAR(255),
    stripe_payment_intent_id VARCHAR(255),
    created_at            TIMESTAMPTZ NOT NULL,
    paid_at               TIMESTAMPTZ,
    return_requested_at   TIMESTAMPTZ,
    returned_at           TIMESTAMPTZ,
    return_reason         TEXT
);

CREATE UNIQUE INDEX idx_orders_stripe_session_id ON shop.orders (stripe_session_id)
    WHERE stripe_session_id IS NOT NULL;
CREATE INDEX idx_orders_user_id ON shop.orders (user_id);

-- unit_price_cents/product_name are snapshotted at checkout time, independent of shop.products.
-- One line per distinct product per order, so (order_id, product_id) is a natural composite key
-- — no separate ordinal/line-index column needed.
CREATE TABLE shop.order_lines (
    order_id          UUID NOT NULL REFERENCES shop.orders (id) ON DELETE CASCADE,
    product_id        UUID NOT NULL,
    product_name      VARCHAR(255) NOT NULL,
    unit_price_cents  BIGINT NOT NULL,
    quantity          INT NOT NULL,
    PRIMARY KEY (order_id, product_id)
);
