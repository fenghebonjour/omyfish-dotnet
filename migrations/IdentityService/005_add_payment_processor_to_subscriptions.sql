-- Which processor (stripe | paypal | adyen) owns this subscription's customer_id/
-- subscription_id — needed now that more than one processor can create a subscription.
ALTER TABLE subscriptions ADD COLUMN IF NOT EXISTS payment_processor VARCHAR(20);
