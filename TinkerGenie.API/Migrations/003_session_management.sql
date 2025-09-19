-- TinkerGenie Session Management Tables
-- Run this migration to enable proper session and thread management

-- Create conversation threads table
CREATE TABLE IF NOT EXISTS conversation_threads (
    thread_id VARCHAR(100) PRIMARY KEY,
    user_id VARCHAR(100) NOT NULL,
    type VARCHAR(50) NOT NULL,
    title VARCHAR(200) NOT NULL,
    status VARCHAR(50) NOT NULL DEFAULT 'Active',
    metadata JSONB DEFAULT '{}',
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

-- Create thread messages table
CREATE TABLE IF NOT EXISTS thread_messages (
    id SERIAL PRIMARY KEY,
    thread_id VARCHAR(100) NOT NULL REFERENCES conversation_threads(thread_id),
    role VARCHAR(20) NOT NULL,
    content TEXT NOT NULL,
    timestamp TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    metadata JSONB DEFAULT '{}'
);

-- Add indexes for performance
CREATE INDEX IF NOT EXISTS idx_threads_user_id ON conversation_threads(user_id);
CREATE INDEX IF NOT EXISTS idx_threads_type ON conversation_threads(type);
CREATE INDEX IF NOT EXISTS idx_threads_status ON conversation_threads(status);
CREATE INDEX IF NOT EXISTS idx_messages_thread_id ON thread_messages(thread_id);
CREATE INDEX IF NOT EXISTS idx_messages_timestamp ON thread_messages(timestamp);

-- Add current_day column to user_data if not exists
ALTER TABLE user_data 
ADD COLUMN IF NOT EXISTS current_day INTEGER DEFAULT 1;

-- Create or update leadership_daily_prompts table
CREATE TABLE IF NOT EXISTS leadership_daily_prompts (
    day_number INTEGER PRIMARY KEY,
    prompt_title VARCHAR(200) NOT NULL,
    prompt_text TEXT NOT NULL,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);

-- Insert sample daily prompts for testing (Days 1-5)
INSERT INTO leadership_daily_prompts (day_number, prompt_title, prompt_text) VALUES
(1, 'Mirror Reflection', 'Look yourself in the mirror for 60 seconds. Ask: "What am I feeling right now, and what''s the honest truth about why?" No fixing—just noticing.'),
(2, 'Energy Audit', 'List three things that drained your energy yesterday and three that gave you energy. What pattern do you see?'),
(3, 'Hard Truth', 'Complete this sentence: "The uncomfortable truth I''ve been avoiding in my business is..." Then sit with it for 2 minutes.'),
(4, 'Success Fear', 'What would change if your business doubled in the next 90 days? Write down what scares you about that success.'),
(5, 'Leadership Shadow', 'Think of a time you disappointed yourself as a leader. What were you afraid of in that moment?')
ON CONFLICT (day_number) DO NOTHING;

-- Create session tracking table
CREATE TABLE IF NOT EXISTS user_sessions (
    session_id VARCHAR(100) PRIMARY KEY,
    user_id VARCHAR(100) NOT NULL,
    session_type VARCHAR(50) NOT NULL,
    started_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    last_active TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    completed_at TIMESTAMP,
    metadata JSONB DEFAULT '{}'
);

-- Create index for session lookups
CREATE INDEX IF NOT EXISTS idx_sessions_user_id ON user_sessions(user_id);
CREATE INDEX IF NOT EXISTS idx_sessions_type ON user_sessions(session_type);
CREATE INDEX IF NOT EXISTS idx_sessions_active ON user_sessions(last_active);

-- Function to update the updated_at timestamp
CREATE OR REPLACE FUNCTION update_updated_at_column()
RETURNS TRIGGER AS $$
BEGIN
    NEW.updated_at = CURRENT_TIMESTAMP;
    RETURN NEW;
END;
$$ language 'plpgsql';

-- Create triggers for updated_at
DROP TRIGGER IF EXISTS update_threads_updated_at ON conversation_threads;
CREATE TRIGGER update_threads_updated_at BEFORE UPDATE ON conversation_threads
    FOR EACH ROW EXECUTE FUNCTION update_updated_at_column();

DROP TRIGGER IF EXISTS update_prompts_updated_at ON leadership_daily_prompts;
CREATE TRIGGER update_prompts_updated_at BEFORE UPDATE ON leadership_daily_prompts
    FOR EACH ROW EXECUTE FUNCTION update_updated_at_column();

-- Grant permissions (adjust as needed for your setup)
GRANT ALL PRIVILEGES ON ALL TABLES IN SCHEMA public TO genie_admin;
GRANT ALL PRIVILEGES ON ALL SEQUENCES IN SCHEMA public TO genie_admin;
