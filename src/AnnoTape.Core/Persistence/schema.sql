-- Canonical schema manifest. Ordered migrations under Migrations/ create this schema.
CREATE TABLE projects (
    id TEXT PRIMARY KEY,
    title TEXT NOT NULL,
    notes TEXT NOT NULL,
    location TEXT NULL,
    created_utc TEXT NOT NULL,
    modified_utc TEXT NOT NULL,
    last_opened_utc TEXT NOT NULL,
    state INTEGER NOT NULL,
    schema_version INTEGER NOT NULL
);

CREATE TABLE photo_documents (
    id TEXT PRIMARY KEY,
    project_id TEXT NOT NULL REFERENCES projects(id) ON DELETE CASCADE,
    ordinal INTEGER NOT NULL,
    source_path TEXT NOT NULL,
    pixel_width INTEGER NOT NULL,
    pixel_height INTEGER NOT NULL,
    rotation_degrees INTEGER NOT NULL,
    revision INTEGER NOT NULL,
    autosaved_utc TEXT NULL
);

CREATE TABLE dimension_annotations (
    id TEXT PRIMARY KEY,
    document_id TEXT NOT NULL REFERENCES photo_documents(id) ON DELETE CASCADE,
    ordinal INTEGER NOT NULL,
    start_x REAL NOT NULL,
    start_y REAL NOT NULL,
    end_x REAL NOT NULL,
    end_y REAL NOT NULL,
    label_x REAL NOT NULL,
    label_y REAL NOT NULL,
    display_text TEXT NOT NULL,
    normalized_millimetres TEXT NOT NULL,
    unit INTEGER NOT NULL,
    precision INTEGER NULL,
    label TEXT NULL,
    style INTEGER NOT NULL,
    created_utc TEXT NOT NULL,
    modified_utc TEXT NOT NULL
);

