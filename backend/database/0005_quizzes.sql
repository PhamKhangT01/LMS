CREATE TABLE IF NOT EXISTS quizzes (
    id uuid PRIMARY KEY, course_id uuid NOT NULL REFERENCES courses(id) ON DELETE RESTRICT,
    class_code varchar(30) NOT NULL, created_by_id uuid NOT NULL REFERENCES users(id) ON DELETE RESTRICT,
    title varchar(300) NOT NULL, description text NOT NULL, duration_minutes integer NOT NULL,
    attempts_allowed integer NOT NULL DEFAULT 1, passing_score numeric(8,2) NOT NULL DEFAULT 0,
    opens_at timestamptz NULL, closes_at timestamptz NULL, status integer NOT NULL DEFAULT 0, created_at timestamptz NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_quizzes_course_class ON quizzes(course_id, class_code);
CREATE TABLE IF NOT EXISTS quiz_questions (
    id uuid PRIMARY KEY, quiz_id uuid NOT NULL REFERENCES quizzes(id) ON DELETE CASCADE,
    content text NOT NULL, type integer NOT NULL, options_json text NOT NULL, correct_answer text NOT NULL,
    points numeric(8,2) NOT NULL, position integer NOT NULL
);
CREATE TABLE IF NOT EXISTS quiz_attempts (
    id uuid PRIMARY KEY, quiz_id uuid NOT NULL REFERENCES quizzes(id) ON DELETE CASCADE,
    student_id uuid NOT NULL REFERENCES users(id) ON DELETE RESTRICT, started_at timestamptz NOT NULL,
    submitted_at timestamptz NOT NULL, auto_score numeric(8,2) NOT NULL, final_score numeric(8,2) NULL
);
CREATE INDEX IF NOT EXISTS ix_quiz_attempts_quiz_student ON quiz_attempts(quiz_id, student_id);
CREATE TABLE IF NOT EXISTS quiz_answers (
    id uuid PRIMARY KEY, attempt_id uuid NOT NULL REFERENCES quiz_attempts(id) ON DELETE CASCADE,
    question_id uuid NOT NULL REFERENCES quiz_questions(id) ON DELETE RESTRICT, answer text NOT NULL, score numeric(8,2) NULL
);
