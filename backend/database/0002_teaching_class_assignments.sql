-- Apply to LMS databases that were initialized before teaching_classes existed.
CREATE TABLE IF NOT EXISTS teaching_classes (
    id uuid PRIMARY KEY,
    code varchar(30) NOT NULL UNIQUE,
    name varchar(200) NOT NULL,
    course_title varchar(300) NOT NULL,
    academic_year varchar(30) NOT NULL,
    primary_lecturer_id uuid NOT NULL REFERENCES users(id) ON DELETE RESTRICT,
    assistant_lecturer_one_id uuid NOT NULL REFERENCES users(id) ON DELETE RESTRICT,
    assistant_lecturer_two_id uuid NOT NULL REFERENCES users(id) ON DELETE RESTRICT,
    assigned_at timestamptz NOT NULL,
    CONSTRAINT ck_teaching_class_distinct_lecturers CHECK (primary_lecturer_id <> assistant_lecturer_one_id AND primary_lecturer_id <> assistant_lecturer_two_id AND assistant_lecturer_one_id <> assistant_lecturer_two_id)
);
