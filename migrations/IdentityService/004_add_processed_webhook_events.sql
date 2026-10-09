-- Dedup for Stripe webhook deliveries. Stripe can and does redeliver the
-- same event; this records each acted-on event id so a redelivery is
-- recognized and skipped instead of re-applying its side effects.
CREATE TABLE IF NOT EXISTS processed_webhook_events (
    event_id        VARCHAR(255) PRIMARY KEY,
    processed_at    TIMESTAMPTZ NOT NULL DEFAULT NOW()
);
