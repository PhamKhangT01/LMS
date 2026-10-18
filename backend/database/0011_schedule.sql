BEGIN;

CREATE TABLE IF NOT EXISTS schedule_events (
    id uuid PRIMARY KEY,
    teaching_class_id uuid NOT NULL REFERENCES teaching_classes(id) ON DELETE RESTRICT,
    type integer NOT NULL CHECK (type IN (0, 1)),
    title varchar(300) NOT NULL,
    room varchar(100) NOT NULL,
    location varchar(300) NOT NULL DEFAULT '',
    notes varchar(4000) NOT NULL DEFAULT '',
    starts_at timestamptz NOT NULL,
    ends_at timestamptz NOT NULL,
    is_cancelled boolean NOT NULL DEFAULT false,
    revision integer NOT NULL DEFAULT 1,
    created_by_id uuid NOT NULL REFERENCES users(id) ON DELETE RESTRICT,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ck_schedule_events_time CHECK (ends_at > starts_at)
);

CREATE INDEX IF NOT EXISTS ix_schedule_events_class_start
    ON schedule_events(teaching_class_id, starts_at);

CREATE TABLE IF NOT EXISTS schedule_changes (
    id uuid PRIMARY KEY,
    schedule_event_id uuid NOT NULL REFERENCES schedule_events(id) ON DELETE RESTRICT,
    actor_id uuid NOT NULL REFERENCES users(id) ON DELETE RESTRICT,
    action varchar(30) NOT NULL,
    snapshot_json jsonb NOT NULL DEFAULT '{}'::jsonb,
    at timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS ix_schedule_changes_event_at
    ON schedule_changes(schedule_event_id, at);

COMMIT;
