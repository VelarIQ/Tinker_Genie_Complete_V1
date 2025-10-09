-- Create user_skill_progression table
CREATE TABLE user_skill_progression (
    id UUID PRIMARY KEY,
    user_id UUID NOT NULL,
    skill_domain VARCHAR(50) NOT NULL,
    skill_level DOUBLE PRECISION NOT NULL,
    measured_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

-- Create index on user_skill_progression
CREATE INDEX IX_user_skill_progression_user_id_skill_domain 
ON user_skill_progression (user_id, skill_domain);

-- Create user_learning_goals table
CREATE TABLE user_learning_goals (
    id UUID PRIMARY KEY,
    user_id UUID NOT NULL,
    learning_goal VARCHAR(500) NOT NULL,
    progress_notes VARCHAR(1000),
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    target_completion_date TIMESTAMP
);

-- Create index on user_learning_goals
CREATE INDEX IX_user_learning_goals_user_id 
ON user_learning_goals (user_id);

-- Add comments to provide context
COMMENT ON TABLE user_skill_progression IS 'Tracks user skill progression across different domains';
COMMENT ON TABLE user_learning_goals IS 'Stores user-defined learning goals and their progress';



