-- PostGIS already enabled in 001; this migration adds helper functions
-- for radius searches and GeoJSON export used by the observation API.

CREATE OR REPLACE FUNCTION observations_within_radius(
    center_lat  DOUBLE PRECISION,
    center_lon  DOUBLE PRECISION,
    radius_km   DOUBLE PRECISION
)
RETURNS TABLE (
    id                UUID,
    species_name      VARCHAR,
    top_confidence    DOUBLE PRECISION,
    latitude          DOUBLE PRECISION,
    longitude         DOUBLE PRECISION,
    observed_at       TIMESTAMPTZ,
    distance_km       DOUBLE PRECISION
) AS $$
BEGIN
    RETURN QUERY
    SELECT
        o.id,
        o.species_name,
        o.top_confidence,
        o.latitude,
        o.longitude,
        o.observed_at,
        ST_Distance(
            o.location::geography,
            ST_SetSRID(ST_MakePoint(center_lon, center_lat), 4326)::geography
        ) / 1000.0 AS distance_km
    FROM observations o
    WHERE o.location IS NOT NULL
      AND ST_DWithin(
            o.location::geography,
            ST_SetSRID(ST_MakePoint(center_lon, center_lat), 4326)::geography,
            radius_km * 1000
          )
    ORDER BY distance_km;
END;
$$ LANGUAGE plpgsql;
