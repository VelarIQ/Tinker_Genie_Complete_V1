-- Migration: Add session management tables for OpenAI-style conversation handling
-- Date: 2025-01-17

-- 1. Conversation threads table
CREATE TABLE IF NOT EXISTS conversation_threads (
    thread_id VARCHAR(255) PRIMARY KEY,
    user_id VARCHAR(255) NOT NULL,
    type VARCHAR(50) NOT NULL, -- DAILY_PROMPT, BURNING_FIRE, GENERAL_CHAT, TINKER_LEVEL
    title VARCHAR(500) NOT NULL,
    status VARCHAR(50) NOT NULL, -- Active, Paused, Completed, Archived
    metadata JSONB,
    created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX IF NOT EXISTS idx_user_threads ON conversation_threads(user_id, updated_at DESC);
CREATE INDEX IF NOT EXISTS idx_thread_type ON conversation_threads(type, status);

-- 2. Thread messages table
CREATE TABLE IF NOT EXISTS thread_messages (
    id SERIAL PRIMARY KEY,
    thread_id VARCHAR(255) NOT NULL,
    role VARCHAR(50) NOT NULL, -- user, assistant, system
    content TEXT NOT NULL,
    metadata JSONB,
    timestamp TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    
    FOREIGN KEY (thread_id) REFERENCES conversation_threads(thread_id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS idx_thread_messages ON thread_messages(thread_id, timestamp);

-- 3. Curriculum resources table (for burning fires links)
CREATE TABLE IF NOT EXISTS curriculum_resources (
    id SERIAL PRIMARY KEY,
    title VARCHAR(500) NOT NULL,
    url TEXT NOT NULL,
    content TEXT,
    topics TEXT[],
    resource_type VARCHAR(100), -- video, document, worksheet, article, guide
    created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX IF NOT EXISTS idx_curriculum_topics ON curriculum_resources USING GIN(topics);
CREATE INDEX IF NOT EXISTS idx_curriculum_type ON curriculum_resources(resource_type);

-- 4. User session state table (for quick session recovery)
CREATE TABLE IF NOT EXISTS user_session_states (
    user_id VARCHAR(255) PRIMARY KEY,
    session_id VARCHAR(255) NOT NULL,
    active_threads JSONB, -- Map of type to thread_id
    current_context VARCHAR(50), -- Current conversation type
    last_active TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    metadata JSONB
);

CREATE INDEX IF NOT EXISTS idx_session_activity ON user_session_states(last_active DESC);

-- 5. Add indexes to existing tables for better performance
CREATE INDEX IF NOT EXISTS idx_user_prompt_deliveries_user_day 
    ON user_prompt_deliveries(user_id, day_number);

CREATE INDEX IF NOT EXISTS idx_conversation_messages_conversation 
    ON conversation_messages(conversation_id, created_at DESC);

-- 6. Add function to automatically update updated_at timestamp
CREATE OR REPLACE FUNCTION update_updated_at_column()
RETURNS TRIGGER AS $$
BEGIN
    NEW.updated_at = CURRENT_TIMESTAMP;
    RETURN NEW;
END;
$$ language 'plpgsql';

-- Add triggers for updated_at
DROP TRIGGER IF EXISTS update_conversation_threads_updated_at ON conversation_threads;
CREATE TRIGGER update_conversation_threads_updated_at 
    BEFORE UPDATE ON conversation_threads 
    FOR EACH ROW EXECUTE FUNCTION update_updated_at_column();

DROP TRIGGER IF EXISTS update_curriculum_resources_updated_at ON curriculum_resources;
CREATE TRIGGER update_curriculum_resources_updated_at 
    BEFORE UPDATE ON curriculum_resources 
    FOR EACH ROW EXECUTE FUNCTION update_updated_at_column();

-- 7. Sample curriculum data for testing burning fires
INSERT INTO curriculum_resources (title, url, content, topics, resource_type) VALUES 
('Crisis Management Guide', 
 'https://twobrain.com/resources/crisis-management', 
 'A comprehensive guide to handling business crises with proven strategies from Two-Brain Business mentors.',
 ARRAY['crisis', 'management', 'leadership', 'emergency'],
 'guide'),
 
('Staff Conflict Resolution Worksheet', 
 'https://twobrain.com/resources/conflict-resolution-worksheet', 
 'Step-by-step worksheet for resolving staff conflicts and improving team dynamics.',
 ARRAY['staff', 'conflict', 'team', 'communication'],
 'worksheet'),

('Financial Recovery Playbook', 
 'https://twobrain.com/resources/financial-recovery', 
 'Proven strategies for recovering from financial setbacks and building resilient revenue streams.',
 ARRAY['finance', 'recovery', 'revenue', 'cashflow'],
 'document'),

('Client Retention Strategies Video', 
 'https://twobrain.com/videos/client-retention', 
 'Chris Cooper explains the key strategies for improving client retention and lifetime value.',
 ARRAY['retention', 'clients', 'customer service', 'loyalty'],
 'video'),

('Leadership Development Path', 
 'https://twobrain.com/resources/leadership-path', 
 'A structured approach to developing leadership skills for business owners.',
 ARRAY['leadership', 'development', 'skills', 'growth'],
 'article')
ON CONFLICT DO NOTHING;
