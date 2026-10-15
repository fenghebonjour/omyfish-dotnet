CREATE SCHEMA IF NOT EXISTS shop;

CREATE TABLE shop.products (
    id              UUID PRIMARY KEY,
    name            VARCHAR(255) NOT NULL,
    description     TEXT,
    price_cents     BIGINT NOT NULL,
    image_url       VARCHAR(512),
    stock_quantity  INT NOT NULL DEFAULT 0,
    active          BOOLEAN NOT NULL DEFAULT TRUE
);

-- Launch catalog: top 10 lure baits for 2026. Placeholder names/prices/images for the MVP —
-- replace with the real catalog before going live.
INSERT INTO shop.products (id, name, description, price_cents, image_url, stock_quantity, active) VALUES
    ('11111111-1111-1111-1111-111111111001', 'ShadowStrike Walking Topwater',
     'A walk-the-dog topwater for big bass, 2026''s breakout surface bait.', 2499,
     '/images/products/shadowstrike-topwater.jpg', 50, TRUE),
    ('11111111-1111-1111-1111-111111111002', 'HyperGlide Swimbait 5"',
     'Ultra-realistic paddle-tail swimbait with a hyper-tight wobble.', 3499,
     '/images/products/hyperglide-swimbait.jpg', 50, TRUE),
    ('11111111-1111-1111-1111-111111111003', 'ChatterVibe Bladed Jig',
     'Bladed jig with a high-frequency chatter — all-conditions reaction bait.', 1899,
     '/images/products/chattervibe-jig.jpg', 50, TRUE),
    ('11111111-1111-1111-1111-111111111004', 'NeonFlash Spinnerbait',
     'High-visibility willow-leaf spinnerbait for stained water.', 1499,
     '/images/products/neonflash-spinnerbait.jpg', 50, TRUE),
    ('11111111-1111-1111-1111-111111111005', 'DeepCrank MX-10 Crankbait',
     'Deep-diving crankbait, 10ft+ at trolling speed, tuned for suspended fish.', 2199,
     '/images/products/deepcrank-mx10.jpg', 50, TRUE),
    ('11111111-1111-1111-1111-111111111006', 'GhostTail Soft Jerkbait',
     'Weightless soft jerkbait with a subtle ghost-tail kick on the pause.', 1299,
     '/images/products/ghosttail-jerkbait.jpg', 50, TRUE),
    ('11111111-1111-1111-1111-111111111007', 'BuzzStorm Buzzbait',
     'Loud dual-blade buzzbait for low-light topwater explosions.', 1699,
     '/images/products/buzzstorm-buzzbait.jpg', 50, TRUE),
    ('11111111-1111-1111-1111-111111111008', 'NedRig Finesse Jig Kit',
     'Finesse jig head + stick-bait combo kit for tough bite conditions.', 999,
     '/images/products/nedrig-finesse-kit.jpg', 50, TRUE),
    ('11111111-1111-1111-1111-111111111009', 'PopFrenzy Popper',
     'Loud-cupped popper that draws explosive surface strikes.', 1599,
     '/images/products/popfrenzy-popper.jpg', 50, TRUE),
    ('11111111-1111-1111-1111-111111111010', 'MegaGlide Pike Glide Bait 8"',
     'Big-profile glide bait built for trophy pike and musky.', 4499,
     '/images/products/megaglide-pike.jpg', 50, TRUE);
