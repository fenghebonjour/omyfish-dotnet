CREATE EXTENSION IF NOT EXISTS postgis;
CREATE EXTENSION IF NOT EXISTS "uuid-ossp";

CREATE TABLE observations (
    id                  UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    user_id             UUID NOT NULL,
    species_name        VARCHAR(255) NOT NULL,
    scientific_name     VARCHAR(255),
    top_confidence      DOUBLE PRECISION NOT NULL,
    image_storage_key   VARCHAR(512) NOT NULL,
    location            GEOMETRY(Point, 4326),
    latitude            DOUBLE PRECISION,
    longitude           DOUBLE PRECISION,
    notes               TEXT,
    exif_captured_at    TIMESTAMPTZ,
    exif_camera_model   VARCHAR(255),
    exif_image_width    INT,
    exif_image_height   INT,
    observed_at         TIMESTAMPTZ NOT NULL,
    created_at          TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at          TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE INDEX idx_observations_location   ON observations USING GIST (location);
CREATE INDEX idx_observations_user_id    ON observations (user_id);
CREATE INDEX idx_observations_species    ON observations (species_name);
CREATE INDEX idx_observations_observed   ON observations (observed_at DESC);

CREATE OR REPLACE FUNCTION update_updated_at()
RETURNS TRIGGER AS $$
BEGIN NEW.updated_at = NOW(); RETURN NEW; END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER trg_observations_updated_at
    BEFORE UPDATE ON observations
    FOR EACH ROW EXECUTE FUNCTION update_updated_at();
