BEGIN;
ALTER TABLE grade_appeals ADD COLUMN IF NOT EXISTS proposed_grades_json text NOT NULL DEFAULT '[]';
DROP INDEX IF EXISTS uq_pending_appeal;
CREATE UNIQUE INDEX uq_pending_appeal ON grade_appeals(attempt_id) WHERE status IN (0,3);
COMMIT;
