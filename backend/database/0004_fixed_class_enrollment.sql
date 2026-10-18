-- Fixed-class model: students belong to a predefined academy class.
-- The department head assigns that class to a course and teaching team;
-- the API automatically enrolls all students whose class_code matches.
ALTER TABLE users ADD COLUMN IF NOT EXISTS class_code varchar(30);
ALTER TABLE teaching_classes ADD COLUMN IF NOT EXISTS course_id uuid;

UPDATE teaching_classes tc
SET course_id = c.id
FROM courses c
WHERE tc.course_id IS NULL AND lower(tc.course_title) = lower(c.title);

-- Preserve the fixed class for already-seeded students when upgrading.
UPDATE users u
SET class_code = tc.code
FROM enrollments e
JOIN teaching_classes tc ON tc.course_id = e.course_id
WHERE u.id = e.user_id
  AND u.role = 3
  AND u.class_code IS NULL;

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_teaching_classes_course') THEN
        ALTER TABLE teaching_classes
            ADD CONSTRAINT fk_teaching_classes_course
            FOREIGN KEY (course_id) REFERENCES courses(id) ON DELETE RESTRICT;
    END IF;
END $$;

CREATE INDEX IF NOT EXISTS ix_teaching_classes_course_id ON teaching_classes(course_id);
