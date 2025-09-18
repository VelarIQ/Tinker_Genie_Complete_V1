-- Analytics Events Table
CREATE TABLE IF NOT EXISTS analytics_events (
    id SERIAL PRIMARY KEY,
    user_id VARCHAR(255) NOT NULL,
    event_type VARCHAR(100) NOT NULL,
    event_data JSONB,
    timestamp TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    session_id VARCHAR(255),
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);

-- Create index for better performance
CREATE INDEX IF NOT EXISTS idx_analytics_events_user_id ON analytics_events(user_id);
CREATE INDEX IF NOT EXISTS idx_analytics_events_timestamp ON analytics_events(timestamp);
CREATE INDEX IF NOT EXISTS idx_analytics_events_event_type ON analytics_events(event_type);

-- User Data Table (if it doesn't exist)
CREATE TABLE IF NOT EXISTS user_data (
    user_id VARCHAR(255) PRIMARY KEY,
    current_day INTEGER DEFAULT 1,
    signup_date TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    last_active TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);

-- User Profiles Table (if it doesn't exist)
CREATE TABLE IF NOT EXISTS user_profiles (
    user_id VARCHAR(255) PRIMARY KEY,
    communication_style VARCHAR(50) DEFAULT 'balanced',
    timezone VARCHAR(100) DEFAULT 'UTC',
    prompt_time TIME DEFAULT '09:00',
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);

-- Leadership Daily Prompts Table (if it doesn't exist)
CREATE TABLE IF NOT EXISTS leadership_daily_prompts (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    day_number INTEGER NOT NULL,
    prompt_title VARCHAR(255) NOT NULL,
    prompt_text TEXT NOT NULL,
    fill_in_blanks TEXT[],
    version INTEGER DEFAULT 1,
    is_active BOOLEAN DEFAULT true,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);

-- User Prompt Progress Table (if it doesn't exist)
CREATE TABLE IF NOT EXISTS user_prompt_progress (
    user_id VARCHAR(255) NOT NULL,
    day_number INTEGER NOT NULL,
    prompt_version INTEGER NOT NULL,
    status VARCHAR(50) DEFAULT 'started',
    responses JSONB,
    started_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP,
    completed_at TIMESTAMP WITH TIME ZONE,
    PRIMARY KEY (user_id, day_number)
);

-- Create indexes for better performance
CREATE INDEX IF NOT EXISTS idx_leadership_prompts_day_number ON leadership_daily_prompts(day_number);
CREATE INDEX IF NOT EXISTS idx_leadership_prompts_active ON leadership_daily_prompts(is_active);
CREATE INDEX IF NOT EXISTS idx_user_progress_user_id ON user_prompt_progress(user_id);
CREATE INDEX IF NOT EXISTS idx_user_progress_day_number ON user_prompt_progress(day_number);

-- Insert some sample data for testing
INSERT INTO leadership_daily_prompts (day_number, prompt_title, prompt_text, fill_in_blanks, version, is_active) VALUES
(1, 'Welcome to Your Leadership Journey', 'Today, take a moment to reflect on your current leadership style. What are your strengths? What areas would you like to improve?', ARRAY['strength', 'improvement_area'], 1, true),
(2, 'Vision and Mission', 'Define your personal mission statement. What do you want to achieve as a leader? How do you want to be remembered?', ARRAY['mission', 'legacy'], 1, true),
(3, 'Communication Skills', 'Practice active listening today. In your next conversation, focus entirely on understanding the other person without thinking about your response.', ARRAY['conversation_topic', 'key_insight'], 1, true),
(4, 'Decision Making', 'Think about a recent decision you made. What process did you use? How could you improve your decision-making process?', ARRAY['decision', 'process_improvement'], 1, true),
(5, 'Team Building', 'Identify one team member you haven''t connected with recently. Make an effort to understand their perspective and challenges.', ARRAY['team_member', 'connection_insight'], 1, true)
ON CONFLICT DO NOTHING;

-- Insert default admin user if it doesn't exist
INSERT INTO user_data (user_id, current_day, signup_date) VALUES
('admin-user', 1, CURRENT_TIMESTAMP)
ON CONFLICT (user_id) DO NOTHING;

INSERT INTO user_profiles (user_id, communication_style, timezone, prompt_time) VALUES
('admin-user', 'balanced', 'UTC', '09:00')
ON CONFLICT (user_id) DO NOTHING;
