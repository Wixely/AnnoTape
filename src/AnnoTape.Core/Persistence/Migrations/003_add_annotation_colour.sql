ALTER TABLE dimension_annotations
ADD COLUMN colour_hex TEXT NOT NULL DEFAULT '#B87333';

UPDATE dimension_annotations
SET colour_hex = CASE style
    WHEN 1 THEN '#FFFFFF'
    WHEN 2 THEN '#111111'
    WHEN 3 THEN '#FFD60A'
    WHEN 4 THEN '#FF453A'
    ELSE '#B87333'
END;
