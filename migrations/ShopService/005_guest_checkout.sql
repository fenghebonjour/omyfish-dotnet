-- Guest checkout: carts and orders can now belong to an anonymous guest (identified by a
-- client-generated cart token / a self-reported email) instead of a logged-in user.

ALTER TABLE shop.carts ALTER COLUMN user_id DROP NOT NULL;
ALTER TABLE shop.carts ADD COLUMN guest_token UUID UNIQUE;
ALTER TABLE shop.carts ADD CONSTRAINT carts_owner_check
    CHECK ((user_id IS NULL) != (guest_token IS NULL));

ALTER TABLE shop.orders ALTER COLUMN user_id DROP NOT NULL;
ALTER TABLE shop.orders ADD COLUMN guest_email VARCHAR(255);
ALTER TABLE shop.orders ADD CONSTRAINT orders_owner_check
    CHECK ((user_id IS NULL) != (guest_email IS NULL));
