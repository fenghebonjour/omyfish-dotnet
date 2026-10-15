CREATE TABLE shop.carts (
    id       UUID PRIMARY KEY,
    user_id  UUID NOT NULL UNIQUE
);

CREATE TABLE shop.cart_items (
    cart_id     UUID NOT NULL REFERENCES shop.carts (id) ON DELETE CASCADE,
    product_id  UUID NOT NULL,
    quantity    INT NOT NULL,
    PRIMARY KEY (cart_id, product_id)
);
