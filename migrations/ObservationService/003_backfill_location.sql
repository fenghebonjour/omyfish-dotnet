-- The `location` geometry column has been dead since 001: the app only ever wrote
-- latitude/longitude, so location stayed NULL and its GIST index and the
-- observations_within_radius() function (002) had nothing to query
-- (BACKLOG.md item F, WEAKNESS_AUDIT.md §3.3). Keep it in sync at the DB layer
-- instead of relying on application code to remember to populate it.

CREATE OR REPLACE FUNCTION sync_observation_location()
RETURNS TRIGGER AS $$
BEGIN
    IF NEW.latitude IS NOT NULL AND NEW.longitude IS NOT NULL THEN
        NEW.location = ST_SetSRID(ST_MakePoint(NEW.longitude, NEW.latitude), 4326);
    ELSE
        NEW.location = NULL;
    END IF;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trg_observations_sync_location ON observations;
CREATE TRIGGER trg_observations_sync_location
    BEFORE INSERT OR UPDATE OF latitude, longitude ON observations
    FOR EACH ROW EXECUTE FUNCTION sync_observation_location();

-- One-time backfill for rows that already have lat/lon from before this trigger existed.
UPDATE observations
SET location = ST_SetSRID(ST_MakePoint(longitude, latitude), 4326)
WHERE location IS NULL AND latitude IS NOT NULL AND longitude IS NOT NULL;
