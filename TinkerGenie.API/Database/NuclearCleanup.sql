-- =====================================================
-- NUCLEAR DATABASE CLEANUP - REMOVE ALL DUPLICATES
-- =====================================================

BEGIN;

-- Step 1: Drop ALL user-related tables except users_clean
DROP TABLE IF EXISTS users CASCADE;
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
DROP TABLE IF EXISTS user_backups CASCADE;
DROP TABLE IF EXISTS user_temp CASCADE;
DROP TABLE IF EXISTS user_cache CASCADE;
DROP TABLE IF EXISTS user_queue CASCADE;
DROP TABLE IF EXISTS user_events CASCADE;
DROP TABLE IF EXISTS user_notifications CASCADE;
DROP TABLE IF EXISTS user_messages CASCADE;
DROP TABLE IF EXISTS user_files CASCADE;
DROP TABLE IF EXISTS user_uploads CASCADE;
DROP TABLE IF EXISTS user_downloads CASCADE;
DROP TABLE IF EXISTS user_exports CASCADE;
DROP TABLE IF EXISTS user_imports CASCADE;
DROP TABLE IF EXISTS user_sync CASCADE;
DROP TABLE IF EXISTS user_migration CASCADE;
DROP TABLE IF EXISTS user_staging CASCADE;
DROP TABLE IF EXISTS user_test CASCADE;
DROP TABLE IF EXISTS user_dev CASCADE;
DROP TABLE IF EXISTS user_prod CASCADE;
DROP TABLE IF EXISTS user_backup CASCADE;
DROP TABLE IF EXISTS user_archive CASCADE;
DROP TABLE IF EXISTS user_deleted CASCADE;
DROP TABLE IF EXISTS user_inactive CASCADE;
DROP TABLE IF EXISTS user_pending CASCADE;
DROP TABLE IF EXISTS user_verified CASCADE;
DROP TABLE IF EXISTS user_unverified CASCADE;
DROP TABLE IF EXISTS user_blocked CASCADE;
DROP TABLE IF EXISTS user_banned CASCADE;
DROP TABLE IF EXISTS user_suspended CASCADE;
DROP TABLE IF EXISTS user_locked CASCADE;
DROP TABLE IF EXISTS user_expired CASCADE;

-- Step 2: Keep ONLY users_clean and user_data (for reference)
-- users_clean is our bulletproof table
-- user_data will be kept as read-only reference

-- Step 3: Create a view that maps user_data to users_clean structure for compatibility
CREATE OR REPLACE VIEW users AS
SELECT 
    id,
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
    updated_at
FROM users_clean;

-- Step 4: Verify our bulletproof table is intact
DO $$
DECLARE
    user_count INTEGER;
BEGIN
    SELECT COUNT(*) INTO user_count FROM users_clean;
    
    IF user_count = 0 THEN
        RAISE EXCEPTION 'NUCLEAR CLEANUP FAILED: users_clean table is empty!';
    END IF;
    
    RAISE NOTICE 'NUCLEAR CLEANUP SUCCESS: users_clean has % users', user_count;
END $$;

COMMIT;

-- Final verification
SELECT 
    'NUCLEAR CLEANUP COMPLETE' as status,
    COUNT(*) as bulletproof_users,
    COUNT(DISTINCT email) as unique_emails
FROM users_clean;



