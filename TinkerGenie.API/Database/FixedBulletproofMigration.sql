-- =====================================================
-- BULLETPROOF DATABASE MIGRATION - ZERO DUPLICATES
-- =====================================================

BEGIN;

-- Step 1: Create bulletproof users table with all necessary constraints
DROP TABLE IF EXISTS bulletproof_users CASCADE;

CREATE TABLE bulletproof_users (
    -- Primary Key
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    
    -- Core Identity (NO DUPLICATES)
    email VARCHAR(255) NOT NULL,
    username VARCHAR(100),
    
    -- Authentication
    password_hash VARCHAR(255),
    requires_password_setup BOOLEAN DEFAULT false,
    requires_password_change BOOLEAN DEFAULT false,
    last_password_change TIMESTAMP,
    failed_login_attempts INTEGER DEFAULT 0,
    account_locked_until TIMESTAMP,
    
    -- Profile
    first_name VARCHAR(100),
    full_name VARCHAR(255),
    display_name VARCHAR(200) GENERATED ALWAYS AS (
        COALESCE(first_name, full_name, username, email)
    ) STORED,
    
    -- Business Info
    business_name VARCHAR(200),
    business_role VARCHAR(100),
    
    -- System
    role VARCHAR(50) DEFAULT 'user',
    is_active BOOLEAN DEFAULT true,
    
    -- Legacy Integration
    user_id VARCHAR(100), -- from user_data.user_id
    external_id VARCHAR(50), -- from user_data.external_id
    entityid INTEGER, -- from user_data.entityid
    google_id VARCHAR(255),
    migration_source VARCHAR(50) DEFAULT 'consolidated',
    
    -- Timestamps
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    last_login TIMESTAMP,
    
    -- BULLETPROOF CONSTRAINTS - NO DUPLICATES POSSIBLE
    CONSTRAINT uk_bulletproof_users_email UNIQUE (email),
    CONSTRAINT uk_bulletproof_users_username UNIQUE (username),
    CONSTRAINT uk_bulletproof_users_user_id UNIQUE (user_id),
    CONSTRAINT uk_bulletproof_users_external_id UNIQUE (external_id),
    CONSTRAINT uk_bulletproof_users_entityid UNIQUE (entityid),
    CONSTRAINT uk_bulletproof_users_google_id UNIQUE (google_id),
    
    -- Data Integrity Constraints
    CONSTRAINT ck_bulletproof_users_email_format CHECK (email ~* '^[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}$'),
    CONSTRAINT ck_bulletproof_users_role CHECK (role IN ('admin', 'user', 'tinker_member')),
    CONSTRAINT ck_bulletproof_users_failed_attempts CHECK (failed_login_attempts >= 0)
);

-- Step 2: Create bulletproof indexes for performance
CREATE INDEX idx_bulletproof_users_email ON bulletproof_users(email);
CREATE INDEX idx_bulletproof_users_active ON bulletproof_users(is_active) WHERE is_active = true;
CREATE INDEX idx_bulletproof_users_role ON bulletproof_users(role);
CREATE INDEX idx_bulletproof_users_external_id ON bulletproof_users(external_id) WHERE external_id IS NOT NULL;
CREATE INDEX idx_bulletproof_users_created ON bulletproof_users(created_at);

-- Step 3: Migrate data from user_data (main source) - NO DUPLICATES
INSERT INTO bulletproof_users (
    email, 
    username, 
    first_name, 
    full_name,
    business_name,
    business_role,
    password_hash,
    user_id,
    external_id,
    entityid,
    role,
    is_active,
    created_at,
    last_login,
    migration_source
)
SELECT DISTINCT ON (LOWER(email))
    LOWER(TRIM(email)) as email,
    NULLIF(TRIM(username), '') as username,
    NULLIF(TRIM(first_name), '') as first_name,
    NULLIF(TRIM(full_name), '') as full_name,
    NULLIF(TRIM(business_name), '') as business_name,
    NULLIF(TRIM(business_role), '') as business_role,
    password_hash,
    user_id,
    external_id,
    entityid,
    CASE 
        WHEN can_access_tinker = true THEN 'tinker_member'
        ELSE 'user'
    END as role,
    COALESCE(is_active, true) as is_active,
    COALESCE(created_at, CURRENT_TIMESTAMP) as created_at,
    last_login,
    'user_data_migration' as migration_source
FROM user_data 
WHERE email IS NOT NULL 
  AND TRIM(email) != ''
  AND email ~* '^[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}$'
ORDER BY LOWER(email), created_at DESC;

-- Step 4: Merge any additional users from 'users' table that aren't duplicates
INSERT INTO bulletproof_users (
    email, 
    username, 
    first_name,
    password_hash,
    requires_password_setup,
    requires_password_change,
    last_password_change,
    role,
    is_active,
    created_at,
    migration_source
)
SELECT DISTINCT ON (LOWER(email))
    LOWER(TRIM(email)) as email,
    NULLIF(TRIM(username), '') as username,
    NULLIF(TRIM(first_name), '') as first_name,
    password_hash,
    COALESCE(requires_password_setup, false),
    COALESCE(requires_password_change, false),
    last_password_change,
    COALESCE(NULLIF(TRIM(role), ''), 'user') as role,
    COALESCE(is_active, true) as is_active,
    COALESCE(created_at, CURRENT_TIMESTAMP) as created_at,
    'users_table_migration' as migration_source
FROM users 
WHERE email IS NOT NULL 
  AND TRIM(email) != ''
  AND email ~* '^[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}$'
  AND LOWER(TRIM(email)) NOT IN (SELECT email FROM bulletproof_users)
ORDER BY LOWER(email), created_at DESC;

-- Step 5: Create update trigger for updated_at
CREATE OR REPLACE FUNCTION update_bulletproof_users_updated_at()
RETURNS TRIGGER AS $$
BEGIN
    NEW.updated_at = CURRENT_TIMESTAMP;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER trg_bulletproof_users_updated_at
    BEFORE UPDATE ON bulletproof_users
    FOR EACH ROW
    EXECUTE FUNCTION update_bulletproof_users_updated_at();

-- Step 6: Verify no duplicates
DO $$
DECLARE
    duplicate_count INTEGER;
    total_users INTEGER;
BEGIN
    SELECT COUNT(*) INTO duplicate_count
    FROM (
        SELECT email, COUNT(*) 
        FROM bulletproof_users 
        GROUP BY email 
        HAVING COUNT(*) > 1
    ) duplicates;
    
    SELECT COUNT(*) INTO total_users FROM bulletproof_users;
    
    IF duplicate_count > 0 THEN
        RAISE EXCEPTION 'MIGRATION FAILED: Found % duplicate emails', duplicate_count;
    END IF;
    
    RAISE NOTICE 'SUCCESS: Migration completed with % users and 0 duplicates', total_users;
END $$;

COMMIT;

-- Final Report
SELECT 
    'BULLETPROOF MIGRATION COMPLETE' as status,
    COUNT(*) as total_users,
    COUNT(DISTINCT email) as unique_emails,
    COUNT(*) - COUNT(DISTINCT email) as duplicates_eliminated,
    COUNT(*) FILTER (WHERE migration_source = 'user_data_migration') as from_user_data,
    COUNT(*) FILTER (WHERE migration_source = 'users_table_migration') as from_users_table
FROM bulletproof_users;



