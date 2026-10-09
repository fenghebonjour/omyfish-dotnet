-- Adyen has no subscription object, so refunding requires the pspReference of the last
-- captured payment rather than a subscription id — recorded here when a payment_captured
-- webhook event fires.
ALTER TABLE subscriptions ADD COLUMN IF NOT EXISTS last_payment_reference VARCHAR(255);
