BEGIN;
ALTER TABLE bank_questions DROP CONSTRAINT IF EXISTS bank_questions_source_check;
ALTER TABLE bank_questions ADD CONSTRAINT bank_questions_source_check CHECK (source BETWEEN 0 AND 3);
COMMIT;
