-- Captured from Stripe's shipping-address collection once a Checkout Session completes —
-- null until then, and null forever for a cancelled order.
ALTER TABLE shop.orders ADD COLUMN shipping_recipient_name VARCHAR(255);
ALTER TABLE shop.orders ADD COLUMN shipping_line1 VARCHAR(255);
ALTER TABLE shop.orders ADD COLUMN shipping_line2 VARCHAR(255);
ALTER TABLE shop.orders ADD COLUMN shipping_city VARCHAR(255);
ALTER TABLE shop.orders ADD COLUMN shipping_state VARCHAR(255);
ALTER TABLE shop.orders ADD COLUMN shipping_postal_code VARCHAR(32);
ALTER TABLE shop.orders ADD COLUMN shipping_country VARCHAR(2);
