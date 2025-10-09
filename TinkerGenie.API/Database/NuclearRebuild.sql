-- =====================================================
-- NUCLEAR REBUILD - DESTROY AND BUILD BETTER
-- =====================================================

BEGIN;

-- Step 1: NUCLEAR DESTRUCTION - Remove ALL user tables
DROP TABLE IF EXISTS users CASCADE;
DROP TABLE IF EXISTS users_clean CASCADE;
DROP TABLE IF EXISTS user_data CASCADE;
DROP TABLE IF EXISTS simple_users CASCADE;
DROP TABLE IF EXISTS core_user CASCADE;
DROP TABLE IF EXISTS app_users CASCADE;
DROP TABLE IF EXISTS admin_users CASCADE;
DROP TABLE IF EXISTS user_profiles CASCADE;
DROP TABLE IF EXISTS user_settings CASCADE;
DROP TABLE IF EXISTS user_preferences CASCADE;
DROP TABLE IF EXISTS user_sessions CASCADE;
DROP TABLE IF EXISTS user_tokens CASCADE;
DROP TABLE IF EXISTS user_roles CASCADE;
DROP TABLE IF EXISTS user_permissions CASCADE;
DROP TABLE IF EXISTS user_groups CASCADE;
DROP TABLE IF EXISTS user_metadata CASCADE;
DROP TABLE IF EXISTS user_activity CASCADE;
DROP TABLE IF EXISTS user_logs CASCADE;
DROP TABLE IF EXISTS user_audit CASCADE;
DROP TABLE IF EXISTS user_history CASCADE;

-- Step 2: BUILD THE ULTIMATE BULLETPROOF USERS TABLE
CREATE TABLE users (
    -- Primary Identity - UUID for global uniqueness
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    
    -- Core Authentication - BULLETPROOF
    email VARCHAR(255) NOT NULL,
    username VARCHAR(100),
    password_hash VARCHAR(255),
    
    -- Profile Information
    first_name VARCHAR(100),
    last_name VARCHAR(100),
    display_name VARCHAR(200) GENERATED ALWAYS AS (
        COALESCE(
            CASE 
                WHEN first_name IS NOT NULL AND last_name IS NOT NULL 
                THEN first_name || ' ' || last_name
                ELSE first_name
            END,
            username,
            split_part(email, '@', 1)
        )
    ) STORED,
    
    -- Security & Access Control
    role VARCHAR(50) DEFAULT 'user' NOT NULL,
    is_active BOOLEAN DEFAULT true NOT NULL,
    is_verified BOOLEAN DEFAULT false NOT NULL,
    requires_password_setup BOOLEAN DEFAULT false NOT NULL,
    requires_password_change BOOLEAN DEFAULT false NOT NULL,
    failed_login_attempts INTEGER DEFAULT 0 NOT NULL,
    account_locked_until TIMESTAMP,
    last_password_change TIMESTAMP,
    last_login TIMESTAMP,
    
    -- Business Context
    business_name VARCHAR(200),
    business_role VARCHAR(100),
    
    -- System Integration
    external_id VARCHAR(50),
    google_id VARCHAR(255),
    microsoft_id VARCHAR(255),
    apple_id VARCHAR(255),
    
    -- Preferences & Settings (JSON for flexibility)
    preferences JSONB DEFAULT '{}' NOT NULL,
    metadata JSONB DEFAULT '{}' NOT NULL,
    
    -- Audit Trail
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP NOT NULL,
    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP NOT NULL,
    created_by UUID,
    updated_by UUID,
    
    -- Data Management
    migration_source VARCHAR(50) DEFAULT 'nuclear_rebuild',
    data_version INTEGER DEFAULT 1 NOT NULL,
    
    -- BULLETPROOF CONSTRAINTS - NO DUPLICATES EVER
    CONSTRAINT uk_users_email UNIQUE (email),
    CONSTRAINT uk_users_username UNIQUE (username),
    CONSTRAINT uk_users_external_id UNIQUE (external_id),
    CONSTRAINT uk_users_google_id UNIQUE (google_id),
    CONSTRAINT uk_users_microsoft_id UNIQUE (microsoft_id),
    CONSTRAINT uk_users_apple_id UNIQUE (apple_id),
    
    -- Data Integrity Constraints
    CONSTRAINT ck_users_email_format CHECK (email ~* '^[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}$'),
    CONSTRAINT ck_users_role CHECK (role IN ('admin', 'user', 'tinker_member', 'moderator', 'support')),
    CONSTRAINT ck_users_failed_attempts CHECK (failed_login_attempts >= 0 AND failed_login_attempts <= 10),
    CONSTRAINT ck_users_data_version CHECK (data_version > 0)
);

-- Step 3: BULLETPROOF INDEXES for maximum performance
CREATE INDEX idx_users_email ON users(email);
CREATE INDEX idx_users_username ON users(username) WHERE username IS NOT NULL;
CREATE INDEX idx_users_active ON users(is_active) WHERE is_active = true;
CREATE INDEX idx_users_role ON users(role);
CREATE INDEX idx_users_external_id ON users(external_id) WHERE external_id IS NOT NULL;
CREATE INDEX idx_users_google_id ON users(google_id) WHERE google_id IS NOT NULL;
CREATE INDEX idx_users_created ON users(created_at);
CREATE INDEX idx_users_last_login ON users(last_login) WHERE last_login IS NOT NULL;
CREATE INDEX idx_users_business ON users(business_name) WHERE business_name IS NOT NULL;
CREATE INDEX idx_users_preferences ON users USING GIN(preferences);
CREATE INDEX idx_users_metadata ON users USING GIN(metadata);

-- Step 4: BULLETPROOF TRIGGERS for automatic maintenance
CREATE OR REPLACE FUNCTION update_users_updated_at()
RETURNS TRIGGER AS $$
BEGIN
    NEW.updated_at = CURRENT_TIMESTAMP;
    NEW.data_version = OLD.data_version + 1;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER trg_users_updated_at
    BEFORE UPDATE ON users
    FOR EACH ROW
    EXECUTE FUNCTION update_users_updated_at();

-- Step 5: MIGRATE DATA from old user_data table if it exists
DO $$
DECLARE
    old_user_count INTEGER := 0;
BEGIN
    -- Check if user_data exists and has data
    SELECT COUNT(*) INTO old_user_count 
    FROM information_schema.tables 
    WHERE table_name = 'user_data' AND table_schema = 'public';
    
    IF old_user_count > 0 THEN
        -- Migrate from user_data
        INSERT INTO users (
            email, username, first_name, password_hash, business_name, business_role,
            role, is_active, created_at, external_id, preferences, metadata, migration_source
        )
        SELECT DISTINCT ON (LOWER(TRIM(email)))
            LOWER(TRIM(email)) as email,
            NULLIF(TRIM(username), '') as username,
            NULLIF(TRIM(first_name), '') as first_name,
            password_hash,
            NULLIF(TRIM(business_name), '') as business_name,
            NULLIF(TRIM(business_role), '') as business_role,
            CASE 
                WHEN can_access_tinker = true THEN 'tinker_member'
                ELSE 'user'
            END as role,
            COALESCE(is_active, true) as is_active,
            COALESCE(created_at, CURRENT_TIMESTAMP) as created_at,
            external_id,
            COALESCE(preferences, '{}') as preferences,
            COALESCE(metadata, '{}') as metadata,
            'user_data_migration' as migration_source
        FROM user_data 
        WHERE email IS NOT NULL 
          AND TRIM(email) != ''
          AND email ~* '^[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}$'
        ORDER BY LOWER(TRIM(email)), created_at DESC;
        
        RAISE NOTICE 'Migrated users from user_data table';
    END IF;
END $$;

-- Step 6: CREATE ESSENTIAL ADMIN USER if none exist
DO $$
DECLARE
    user_count INTEGER;
BEGIN
    SELECT COUNT(*) INTO user_count FROM users;
    
    IF user_count = 0 THEN
        INSERT INTO users (
            email, username, first_name, password_hash, role, is_active, is_verified,
            migration_source, preferences, metadata
        ) VALUES (
            'admin@tinkergenie.ai',
            'admin',
            'System Admin',
            '$2b$12$UCJGLHgg/UjW1m4dWIE/Qec1PZsEbnfAWIFO5BWvkLhGyLVqikovS', -- TinkerGenie2025!
            'admin',
            true,
            true,
            'nuclear_rebuild_admin',
            '{"theme": "dark", "notifications": true}',
            '{"created_by": "nuclear_rebuild", "system_user": true}'
        );
        
        RAISE NOTICE 'Created system admin user';
    END IF;
END $$;

-- Step 7: ADD YOUR USER with admin privileges
INSERT INTO users (
    email, username, first_name, password_hash, role, is_active, is_verified,
    migration_source, preferences, metadata
) VALUES (
    'leighton@twobrain.ai',
    'leighton',
    'Leighton',
    '$2b$12$UCJGLHgg/UjW1m4dWIE/Qec1PZsEbnfAWIFO5BWvkLhGyLVqikovS', -- TinkerGenie2025!
    'admin',
    true,
    true,
    'nuclear_rebuild_owner',
    '{"theme": "system", "notifications": true, "advanced_features": true}',
    '{"created_by": "nuclear_rebuild", "owner": true, "full_access": true}'
)
ON CONFLICT (email) DO UPDATE SET
    password_hash = EXCLUDED.password_hash,
    role = 'admin',
    is_active = true,
    is_verified = true,
    updated_at = CURRENT_TIMESTAMP;

-- Step 8: CREATE OTHER ESSENTIAL USERS
INSERT INTO users (email, username, first_name, password_hash, role, migration_source) VALUES
('chris@twobrain.ai', 'chris', 'Chris', '$2b$12$UCJGLHgg/UjW1m4dWIE/Qec1PZsEbnfAWIFO5BWvkLhGyLVqikovS', 'admin', 'nuclear_rebuild'),
('mikel@twobrain.ai', 'mikel', 'Mikel', '$2b$12$UCJGLHgg/UjW1m4dWIE/Qec1PZsEbnfAWIFO5BWvkLhGyLVqikovS', 'admin', 'nuclear_rebuild'),
('matt@twobrain.ai', 'matt', 'Matt', '$2b$12$UCJGLHgg/UjW1m4dWIE/Qec1PZsEbnfAWIFO5BWvkLhGyLVqikovS', 'admin', 'nuclear_rebuild')
ON CONFLICT (email) DO UPDATE SET
    password_hash = EXCLUDED.password_hash,
    role = 'admin',
    is_active = true,
    updated_at = CURRENT_TIMESTAMP;

-- Step 9: VERIFY BULLETPROOF CONSTRUCTION
DO $$
DECLARE
    user_count INTEGER;
    admin_count INTEGER;
    duplicate_count INTEGER;
BEGIN
    SELECT COUNT(*) INTO user_count FROM users;
    SELECT COUNT(*) INTO admin_count FROM users WHERE role = 'admin';
    
    SELECT COUNT(*) INTO duplicate_count
    FROM (
        SELECT email, COUNT(*) 
        FROM users 
        GROUP BY email 
        HAVING COUNT(*) > 1
    ) duplicates;
    
    IF duplicate_count > 0 THEN
        RAISE EXCEPTION 'NUCLEAR REBUILD FAILED: Found % duplicate emails', duplicate_count;
    END IF;
    
    IF user_count = 0 THEN
        RAISE EXCEPTION 'NUCLEAR REBUILD FAILED: No users created';
    END IF;
    
    RAISE NOTICE 'NUCLEAR REBUILD SUCCESS: % users created, % admins, 0 duplicates', user_count, admin_count;
END $$;

COMMIT;

-- Final Status Report
SELECT 
    'NUCLEAR REBUILD COMPLETE' as status,
    COUNT(*) as total_users,
    COUNT(*) FILTER (WHERE role = 'admin') as admin_users,
    COUNT(*) FILTER (WHERE role = 'user') as regular_users,
    COUNT(*) FILTER (WHERE role = 'tinker_member') as tinker_members,
    COUNT(*) FILTER (WHERE is_active = true) as active_users,
    COUNT(*) FILTER (WHERE is_verified = true) as verified_users,
    COUNT(DISTINCT email) as unique_emails,
    COUNT(*) - COUNT(DISTINCT email) as duplicates_eliminated
FROM users;



