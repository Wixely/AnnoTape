ALTER TABLE dimension_annotations
ADD COLUMN label_centered INTEGER NOT NULL DEFAULT 0;

UPDATE dimension_annotations
SET label_centered = 1
WHERE abs(label_x - ((start_x + end_x) / 2.0)) < 0.0001
  AND abs(label_y - (((start_y + end_y) / 2.0) - 0.05)) < 0.0001;
