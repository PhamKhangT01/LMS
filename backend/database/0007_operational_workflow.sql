BEGIN;
ALTER TABLE users ADD COLUMN IF NOT EXISTS is_active boolean NOT NULL DEFAULT true;
ALTER TABLE users ADD COLUMN IF NOT EXISTS must_change_password boolean NOT NULL DEFAULT true;
ALTER TABLE users ADD COLUMN IF NOT EXISTS token_version integer NOT NULL DEFAULT 0;
ALTER TABLE teaching_classes ADD COLUMN IF NOT EXISTS term varchar(30) NOT NULL DEFAULT 'HK1';
ALTER TABLE teaching_classes ADD COLUMN IF NOT EXISTS status integer NOT NULL DEFAULT 0;
ALTER TABLE teaching_classes ADD COLUMN IF NOT EXISTS starts_at timestamptz;
ALTER TABLE teaching_classes ADD COLUMN IF NOT EXISTS ends_at timestamptz;
ALTER TABLE teaching_classes ADD COLUMN IF NOT EXISTS passing_grade numeric(5,2) NOT NULL DEFAULT 5;
ALTER TABLE teaching_classes ADD COLUMN IF NOT EXISTS required_progress integer NOT NULL DEFAULT 100;
ALTER TABLE teaching_classes ADD COLUMN IF NOT EXISTS appeal_days integer NOT NULL DEFAULT 7;
ALTER TABLE teaching_classes ADD COLUMN IF NOT EXISTS grade_status integer NOT NULL DEFAULT 0;
ALTER TABLE teaching_classes ADD COLUMN IF NOT EXISTS grades_published_at timestamptz;
ALTER TABLE teaching_classes ADD COLUMN IF NOT EXISTS revision integer NOT NULL DEFAULT 0;
ALTER TABLE teaching_classes DROP CONSTRAINT IF EXISTS teaching_classes_code_key;
DROP INDEX IF EXISTS ix_teaching_classes_code;
CREATE UNIQUE INDEX IF NOT EXISTS uq_offering_term ON teaching_classes(code, course_id, academic_year, term);
CREATE TABLE IF NOT EXISTS offering_enrollments (
 id uuid PRIMARY KEY, teaching_class_id uuid NOT NULL REFERENCES teaching_classes(id) ON DELETE RESTRICT,
 user_id uuid NOT NULL REFERENCES users(id) ON DELETE RESTRICT, status integer NOT NULL DEFAULT 0,
 enrolled_at timestamptz NOT NULL DEFAULT now(), UNIQUE(teaching_class_id,user_id)
);
-- Snapshot membership from current class roster. Legacy course-level records remain intact.
INSERT INTO offering_enrollments(id,teaching_class_id,user_id,enrolled_at)
SELECT gen_random_uuid(),tc.id,u.id,now() FROM teaching_classes tc JOIN users u ON u.class_code=tc.code AND u.role=3
ON CONFLICT(teaching_class_id,user_id) DO NOTHING;
CREATE TABLE IF NOT EXISTS lesson_completions (
 id uuid PRIMARY KEY, enrollment_id uuid NOT NULL REFERENCES offering_enrollments(id) ON DELETE RESTRICT,
 lesson_id uuid NOT NULL REFERENCES lessons(id) ON DELETE RESTRICT, completed_at timestamptz NOT NULL DEFAULT now(), UNIQUE(enrollment_id,lesson_id)
);
ALTER TABLE quizzes ADD COLUMN IF NOT EXISTS teaching_class_id uuid REFERENCES teaching_classes(id) ON DELETE RESTRICT;
ALTER TABLE quizzes ADD COLUMN IF NOT EXISTS weight numeric(5,2) NOT NULL DEFAULT 100;
ALTER TABLE quizzes ADD COLUMN IF NOT EXISTS revision integer NOT NULL DEFAULT 0;
-- Map only an unambiguous legacy assignment; stop rather than guess a semester.
UPDATE quizzes q SET teaching_class_id=tc.id FROM teaching_classes tc
WHERE q.teaching_class_id IS NULL AND tc.course_id=q.course_id AND tc.code=q.class_code
AND (SELECT count(*) FROM teaching_classes t WHERE t.course_id=q.course_id AND t.code=q.class_code)=1;
DO $$ BEGIN IF EXISTS (SELECT 1 FROM quizzes WHERE teaching_class_id IS NULL) THEN
 RAISE EXCEPTION 'Unmapped legacy quizzes: resolve course/class/term before upgrading'; END IF; END $$;
ALTER TABLE quiz_attempts ALTER COLUMN submitted_at DROP NOT NULL;
ALTER TABLE quiz_attempts ADD COLUMN IF NOT EXISTS expires_at timestamptz;
ALTER TABLE quiz_attempts ADD COLUMN IF NOT EXISTS status integer NOT NULL DEFAULT 0;
ALTER TABLE quiz_attempts ADD COLUMN IF NOT EXISTS revision integer NOT NULL DEFAULT 0;
UPDATE quiz_attempts a SET expires_at=a.started_at+q.duration_minutes*interval '1 minute',status=CASE WHEN a.final_score IS NULL THEN 1 ELSE 2 END FROM quizzes q WHERE a.quiz_id=q.id AND a.expires_at IS NULL;
ALTER TABLE quiz_attempts ALTER COLUMN expires_at SET NOT NULL;
ALTER TABLE quiz_answers ADD COLUMN IF NOT EXISTS feedback text NOT NULL DEFAULT '';
CREATE UNIQUE INDEX IF NOT EXISTS uq_attempt_in_progress ON quiz_attempts(quiz_id,student_id) WHERE status=0;
CREATE UNIQUE INDEX IF NOT EXISTS uq_attempt_answer ON quiz_answers(attempt_id,question_id);
CREATE TABLE IF NOT EXISTS audit_entries(id uuid PRIMARY KEY, actor_id uuid NOT NULL,entity_id uuid NOT NULL,action text NOT NULL,detail text NOT NULL,at timestamptz NOT NULL DEFAULT now());
CREATE INDEX IF NOT EXISTS ix_audit_entity ON audit_entries(entity_id,at);
CREATE TABLE IF NOT EXISTS grade_appeals(id uuid PRIMARY KEY,teaching_class_id uuid NOT NULL REFERENCES teaching_classes(id) ON DELETE RESTRICT,student_id uuid NOT NULL REFERENCES users(id),attempt_id uuid NOT NULL REFERENCES quiz_attempts(id) ON DELETE RESTRICT,reason text NOT NULL,resolution text NOT NULL DEFAULT '',reviewer_id uuid REFERENCES users(id),status integer NOT NULL DEFAULT 0,created_at timestamptz NOT NULL DEFAULT now(),revision integer NOT NULL DEFAULT 0);
CREATE UNIQUE INDEX IF NOT EXISTS uq_pending_appeal ON grade_appeals(attempt_id) WHERE status=0;
COMMIT;
