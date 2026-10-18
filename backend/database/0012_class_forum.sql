BEGIN;

CREATE TABLE IF NOT EXISTS forum_topics (
    id uuid PRIMARY KEY,
    teaching_class_id uuid NOT NULL REFERENCES teaching_classes(id) ON DELETE RESTRICT,
    author_id uuid NOT NULL REFERENCES users(id) ON DELETE RESTRICT,
    title varchar(200) NOT NULL,
    body varchar(10000) NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    last_activity_at timestamptz NOT NULL DEFAULT now(),
    revision integer NOT NULL DEFAULT 1
);
CREATE INDEX IF NOT EXISTS ix_forum_topics_class_activity
    ON forum_topics(teaching_class_id, last_activity_at DESC);

CREATE TABLE IF NOT EXISTS forum_replies (
    id uuid PRIMARY KEY,
    topic_id uuid NOT NULL REFERENCES forum_topics(id) ON DELETE RESTRICT,
    author_id uuid NOT NULL REFERENCES users(id) ON DELETE RESTRICT,
    body varchar(10000) NOT NULL,
    is_pinned_answer boolean NOT NULL DEFAULT false,
    created_at timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS ix_forum_replies_topic ON forum_replies(topic_id, created_at);
CREATE UNIQUE INDEX IF NOT EXISTS uq_forum_one_pinned_answer
    ON forum_replies(topic_id) WHERE is_pinned_answer;

CREATE TABLE IF NOT EXISTS forum_subscriptions (
    topic_id uuid NOT NULL REFERENCES forum_topics(id) ON DELETE CASCADE,
    user_id uuid NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    subscribed_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY(topic_id, user_id)
);

COMMIT;
