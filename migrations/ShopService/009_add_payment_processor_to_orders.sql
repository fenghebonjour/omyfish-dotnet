-- Multi-acquirer support: records which processor (stripe/paypal/adyen) owns an order's
-- stripe_session_id/stripe_payment_intent_id, matching omyfish-java's V8 and IdentityService's
-- 005 for subscriptions.
ALTER TABLE shop.orders ADD COLUMN IF NOT EXISTS payment_processor VARCHAR(20);

UPDATE shop.orders
SET payment_processor = 'stripe'
WHERE stripe_session_id IS NOT NULL AND payment_processor IS NULL;
