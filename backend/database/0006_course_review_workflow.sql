BEGIN;
ALTER TABLE courses ADD COLUMN IF NOT EXISTS status integer NOT NULL DEFAULT 0;
ALTER TABLE courses ADD COLUMN IF NOT EXISTS revision integer NOT NULL DEFAULT 0;
ALTER TABLE courses ADD COLUMN IF NOT EXISTS review_history_json text NOT NULL DEFAULT '[]';
-- Preserve publication of existing courses; do not claim they passed the new review.
UPDATE courses SET status = 2 WHERE is_published AND status = 0;
COMMIT;
