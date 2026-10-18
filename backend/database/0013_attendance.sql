BEGIN;

CREATE TABLE IF NOT EXISTS attendance_records (
    id uuid PRIMARY KEY,
    schedule_event_id uuid NOT NULL REFERENCES schedule_events(id) ON DELETE RESTRICT,
    student_id uuid NOT NULL REFERENCES users(id) ON DELETE RESTRICT,
    status integer NOT NULL CHECK (status IN (0, 1, 2)),
    marked_by_id uuid NOT NULL REFERENCES users(id) ON DELETE RESTRICT,
    marked_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT uq_attendance_event_student UNIQUE (schedule_event_id, student_id)
);

CREATE INDEX IF NOT EXISTS ix_attendance_student_event
    ON attendance_records(student_id, schedule_event_id);

COMMIT;
