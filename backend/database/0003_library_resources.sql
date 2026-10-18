CREATE TABLE IF NOT EXISTS library_resources (
    id uuid PRIMARY KEY,
    title varchar(300) NOT NULL,
    description text NOT NULL,
    category varchar(100) NOT NULL,
    original_file_name varchar(300) NOT NULL,
    stored_file_name varchar(300) NOT NULL UNIQUE,
    content_type varchar(150) NOT NULL,
    size_bytes bigint NOT NULL,
    uploaded_by_id uuid NOT NULL REFERENCES users(id) ON DELETE RESTRICT,
    uploaded_at timestamptz NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_library_resources_uploaded_at ON library_resources(uploaded_at DESC);
