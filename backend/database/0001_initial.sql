-- LMS Khoa Luật baseline schema for PostgreSQL 14+.
-- Apply once per empty LMS database before starting the API.
CREATE TABLE IF NOT EXISTS users (
    id uuid PRIMARY KEY,
    full_name varchar(200) NOT NULL,
    email varchar(256) NOT NULL UNIQUE,
    password_hash text NOT NULL,
    role integer NOT NULL,
    class_code varchar(30) NULL,
    created_at timestamptz NOT NULL
);

CREATE TABLE IF NOT EXISTS courses (
    id uuid PRIMARY KEY,
    code varchar(30) NOT NULL UNIQUE,
    title varchar(300) NOT NULL,
    description text NOT NULL,
    category text NOT NULL,
    color text NOT NULL,
    is_published boolean NOT NULL,
    lecturer_id uuid NOT NULL REFERENCES users(id) ON DELETE RESTRICT,
    created_at timestamptz NOT NULL
);

CREATE TABLE IF NOT EXISTS lessons (
    id uuid PRIMARY KEY,
    course_id uuid NOT NULL REFERENCES courses(id) ON DELETE CASCADE,
    title varchar(300) NOT NULL,
    content text NOT NULL,
    position integer NOT NULL,
    duration_minutes integer NOT NULL
);

CREATE TABLE IF NOT EXISTS enrollments (
    id uuid PRIMARY KEY,
    user_id uuid NOT NULL REFERENCES users(id),
    course_id uuid NOT NULL REFERENCES courses(id),
    progress_percent integer NOT NULL DEFAULT 0,
    enrolled_at timestamptz NOT NULL,
    CONSTRAINT uq_enrollments_user_course UNIQUE (user_id, course_id)
);

CREATE INDEX IF NOT EXISTS ix_courses_lecturer_id ON courses(lecturer_id);
CREATE INDEX IF NOT EXISTS ix_lessons_course_id ON lessons(course_id);
CREATE INDEX IF NOT EXISTS ix_enrollments_user_id ON enrollments(user_id);

CREATE TABLE IF NOT EXISTS teaching_classes (
    id uuid PRIMARY KEY,
    code varchar(30) NOT NULL UNIQUE,
    name varchar(200) NOT NULL,
    course_id uuid NOT NULL REFERENCES courses(id) ON DELETE RESTRICT,
    course_title varchar(300) NOT NULL,
    academic_year varchar(30) NOT NULL,
    primary_lecturer_id uuid NOT NULL REFERENCES users(id) ON DELETE RESTRICT,
    assistant_lecturer_one_id uuid NOT NULL REFERENCES users(id) ON DELETE RESTRICT,
    assistant_lecturer_two_id uuid NOT NULL REFERENCES users(id) ON DELETE RESTRICT,
    assigned_at timestamptz NOT NULL,
    CONSTRAINT ck_teaching_class_distinct_lecturers CHECK (primary_lecturer_id <> assistant_lecturer_one_id AND primary_lecturer_id <> assistant_lecturer_two_id AND assistant_lecturer_one_id <> assistant_lecturer_two_id)
);
