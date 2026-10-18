BEGIN;
CREATE TABLE IF NOT EXISTS bank_questions (
 id uuid PRIMARY KEY,
 course_id uuid NOT NULL REFERENCES courses(id) ON DELETE RESTRICT,
 created_by_id uuid NOT NULL REFERENCES users(id) ON DELETE RESTRICT,
 content text NOT NULL,
 type integer NOT NULL CHECK(type BETWEEN 0 AND 3),
 options_json text NOT NULL DEFAULT '[]', correct_answer text NOT NULL DEFAULT '',
 points numeric(8,2) NOT NULL CHECK(points > 0), topic varchar(200) NOT NULL DEFAULT '',
 difficulty integer NOT NULL DEFAULT 1 CHECK(difficulty BETWEEN 0 AND 2),
 source integer NOT NULL CHECK(source BETWEEN 0 AND 2), fingerprint varchar(64) NOT NULL,
 is_archived boolean NOT NULL DEFAULT false, revision integer NOT NULL DEFAULT 1,
 created_at timestamptz NOT NULL DEFAULT now(), updated_at timestamptz NOT NULL DEFAULT now(),
 UNIQUE(course_id,created_by_id,fingerprint)
);
CREATE INDEX IF NOT EXISTS ix_bank_course_filter ON bank_questions(course_id,is_archived,type,difficulty);
ALTER TABLE quizzes ADD COLUMN IF NOT EXISTS shuffle_questions boolean NOT NULL DEFAULT false;
ALTER TABLE quizzes ADD COLUMN IF NOT EXISTS shuffle_options boolean NOT NULL DEFAULT false;
ALTER TABLE quiz_attempts ADD COLUMN IF NOT EXISTS question_order_json text NOT NULL DEFAULT '[]';
ALTER TABLE quiz_attempts ADD COLUMN IF NOT EXISTS option_order_json text NOT NULL DEFAULT '{}';
COMMIT;
