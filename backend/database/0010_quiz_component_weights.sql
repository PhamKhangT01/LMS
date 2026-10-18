BEGIN;

ALTER TABLE quizzes
    ADD COLUMN IF NOT EXISTS multiple_choice_weight numeric(5,2) NOT NULL DEFAULT 50,
    ADD COLUMN IF NOT EXISTS essay_weight numeric(5,2) NOT NULL DEFAULT 50;

-- Preserve the score distribution already implied by question points in existing quizzes.
WITH maxima AS (
    SELECT q.id,
           COALESCE(SUM(qq.points) FILTER (WHERE qq.type <> 3), 0) AS multiple_max,
           COALESCE(SUM(qq.points) FILTER (WHERE qq.type = 3), 0) AS essay_max
    FROM quizzes q
    LEFT JOIN quiz_questions qq ON qq.quiz_id = q.id
    GROUP BY q.id
)
UPDATE quizzes q
SET multiple_choice_weight = CASE WHEN m.multiple_max + m.essay_max = 0 THEN 50
                                  ELSE ROUND(m.multiple_max * 100 / (m.multiple_max + m.essay_max), 2) END,
    essay_weight = CASE WHEN m.multiple_max + m.essay_max = 0 THEN 50
                        ELSE 100 - ROUND(m.multiple_max * 100 / (m.multiple_max + m.essay_max), 2) END
FROM maxima m
WHERE q.id = m.id;

ALTER TABLE quizzes
    ADD CONSTRAINT ck_quizzes_component_weights
    CHECK (multiple_choice_weight BETWEEN 0 AND 100 AND essay_weight BETWEEN 0 AND 100 AND multiple_choice_weight + essay_weight = 100);

COMMIT;
