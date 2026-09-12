-- Species catalog moved to MongoDB (BACKLOG.md item E) — read-mostly, flexible-schema
-- reference data with no relational integrity needs. Predictions stay here since they still
-- commit in the same transaction as the MassTransit outbox message on every /identify call
-- (§2.3), which MongoDB can't take part in; the species_id FK/column it used to carry is now
-- dead (Prediction already stores scientific_name directly) so it's dropped along with the
-- table it referenced.
ALTER TABLE predictions DROP CONSTRAINT IF EXISTS predictions_species_id_fkey;
ALTER TABLE predictions DROP COLUMN IF EXISTS species_id;
DROP TABLE IF EXISTS species CASCADE;
