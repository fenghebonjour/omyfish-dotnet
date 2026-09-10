-- Lets consumers dedupe a redelivered integration event instead of creating a duplicate
-- notification (BACKLOG.md item F, WEAKNESS_AUDIT.md §2.4).
ALTER TABLE notifications
    ADD COLUMN IF NOT EXISTS source_event_id UUID NOT NULL DEFAULT gen_random_uuid();

ALTER TABLE notifications
    ALTER COLUMN source_event_id DROP DEFAULT;

CREATE UNIQUE INDEX IF NOT EXISTS uq_notifications_source_event_id
    ON notifications (source_event_id);
