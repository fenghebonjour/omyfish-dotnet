-- Replayable audit ledger built from the omyfish.shop.orders Kafka topic by
-- OrderEventLedgerConsumer — one row per order line. Same shape as omyfish-java's
-- V10__add_order_event_ledger.sql (event_id is a UUID here rather than VARCHAR(64)).
CREATE TABLE IF NOT EXISTS shop.order_event_ledger (
    id               UUID         PRIMARY KEY,
    event_id         UUID         NOT NULL,
    order_id         UUID         NOT NULL,
    product_id       UUID         NOT NULL,
    product_name     VARCHAR(255) NOT NULL,
    quantity         INT          NOT NULL,
    unit_price_cents BIGINT       NOT NULL,
    received_at      TIMESTAMPTZ  NOT NULL DEFAULT NOW()
);

-- One event carries multiple lines — dedup is per (event, product), not just per event.
CREATE UNIQUE INDEX IF NOT EXISTS idx_order_event_ledger_event_product
    ON shop.order_event_ledger (event_id, product_id);

CREATE INDEX IF NOT EXISTS idx_order_event_ledger_order_id
    ON shop.order_event_ledger (order_id);
