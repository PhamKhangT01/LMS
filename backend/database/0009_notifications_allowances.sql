BEGIN;
CREATE TABLE IF NOT EXISTS notifications(id uuid PRIMARY KEY,user_id uuid NOT NULL REFERENCES users(id),teaching_class_id uuid NOT NULL REFERENCES teaching_classes(id),dedup_key text NOT NULL,message text NOT NULL,created_at timestamptz NOT NULL DEFAULT now(),read_at timestamptz,UNIQUE(user_id,dedup_key));
CREATE TABLE IF NOT EXISTS quiz_allowances(id uuid PRIMARY KEY,quiz_id uuid NOT NULL REFERENCES quizzes(id) ON DELETE RESTRICT,student_id uuid NOT NULL REFERENCES users(id),extra_attempts integer NOT NULL DEFAULT 0 CHECK(extra_attempts BETWEEN 0 AND 10),extended_until timestamptz,UNIQUE(quiz_id,student_id));
COMMIT;
