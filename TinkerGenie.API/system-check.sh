#!/bin/bash

echo "🔍 TINKERGENIE SYSTEM STATUS CHECKS"
echo "===================================="
echo ""

# Configuration
DB_SERVER="161.35.5.159"
API_DIR="/var/www/tinker-genie/TinkerGenie.API"

echo "📊 DATABASE STATUS CHECK"
echo "========================"
echo "Checking existing tables..."

PGPASSWORD=***PASSWORD*** psql -h $DB_SERVER -U genie_admin -d tinker_genie << 'EOSQL'
-- List all tables
SELECT table_name, table_type 
FROM information_schema.tables 
WHERE table_schema = 'public' 
ORDER BY table_name;

-- Check user_data structure
\d user_data;

-- Check user_profiles structure  
\d user_profiles;

-- Check conversation_messages structure
\d conversation_messages;

-- Check if leadership prompts table exists
SELECT EXISTS (
   SELECT FROM information_schema.tables 
   WHERE table_schema = 'public' 
   AND table_name = 'leadership_daily_prompts'
) as leadership_prompts_exists;

-- Count existing prompts
SELECT COUNT(*) as prompt_count FROM leadership_daily_prompts WHERE true;

-- Check user count
SELECT COUNT(*) as user_count FROM user_data;

-- Check conversation count
SELECT COUNT(*) as conversation_count FROM conversation_messages;

EOSQL

echo ""
echo "🚀 API SERVICE STATUS"
echo "===================="

# Check if service is running
if systemctl is-active --quiet tinker-api.service; then
    echo "✅ TinkerGenie API Service: RUNNING"
else
    echo "❌ TinkerGenie API Service: STOPPED"
fi

# Check service logs
echo ""
echo "📝 Recent API Logs (last 10 lines):"
sudo journalctl -u tinker-api.service -n 10 --no-pager

echo ""
echo "🗂️ FILE SYSTEM STATUS"
echo "====================="

# Check API directory structure
echo "API Directory contents:"
ls -la $API_DIR/ || echo "API directory not found"

echo ""
echo "Controllers:"
ls -la $API_DIR/Controllers/ 2>/dev/null || echo "Controllers directory not found"

echo ""
echo "Models:"
ls -la $API_DIR/Models/ 2>/dev/null || echo "Models directory not found"

echo ""
echo "🌐 NETWORK STATUS"
echo "=================="

# Check if API is responding
echo "Testing API endpoints:"
echo ""

# Test basic health endpoint
echo "1. Health check:"
curl -s http://localhost:5000/api/health 2>/dev/null | head -n 5 || echo "Health endpoint not responding"

echo ""
echo "2. Test chat endpoint:"
curl -s -X POST http://localhost:5000/api/chat \
  -H "Content-Type: application/json" \
  -d '{"message":"test"}' 2>/dev/null | head -n 5 || echo "Chat endpoint not responding"

echo ""
echo "3. Test auth endpoint:"
curl -s -X POST http://localhost:5000/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"test","password":"test"}' 2>/dev/null | head -n 5 || echo "Auth endpoint not responding"

echo ""
echo "☁️ REDIS STATUS"
echo "==============="

# Check Redis
if redis-cli -a ***PASSWORD*** ping &>/dev/null; then
    echo "✅ Redis: CONNECTED"
    echo "Redis info:"
    redis-cli -a ***PASSWORD*** info memory | grep used_memory_human
else
    echo "❌ Redis: NOT CONNECTED"
fi

echo ""
echo "🧠 WEAVIATE STATUS"
echo "=================="

# Check if Weaviate is configured
if [ -f "$API_DIR/appsettings.json" ]; then
    echo "Checking Weaviate configuration in appsettings.json:"
    grep -A 5 -B 2 -i "weaviate" $API_DIR/appsettings.json || echo "No Weaviate config found"
else
    echo "No appsettings.json found"
fi

echo ""
echo "📱 PWA STATUS"
echo "============="

# Check PWA files
if [ -f "/var/www/html/index.html" ]; then
    echo "✅ PWA files found at /var/www/html/"
    ls -la /var/www/html/ | head -10
else
    echo "❌ No PWA files found"
fi

echo ""
echo "🔗 EXTERNAL CONNECTIONS"
echo "======================="

# Test external connections
echo "1. Testing OpenAI API connectivity:"
if [ -f "$API_DIR/appsettings.json" ]; then
    # Extract OpenAI key (first 10 chars for security)
    OPENAI_KEY=$(grep -o '"OpenAI.*ApiKey"[^"]*"[^"]*"' $API_DIR/appsettings.json | cut -d'"' -f4 | head -c 10)
    if [ ! -z "$OPENAI_KEY" ]; then
        echo "OpenAI API Key found (starts with: ${OPENAI_KEY}...)"
    else
        echo "❌ No OpenAI API Key found"
    fi
else
    echo "❌ Cannot check OpenAI config - no appsettings.json"
fi

echo ""
echo "2. Testing Google Drive connectivity:"
if [ -f "$API_DIR/service-account-key.json" ]; then
    echo "✅ Google Drive service account key found"
else
    echo "❌ No Google Drive service account key found"
fi

echo ""
echo "📊 SUMMARY"
echo "=========="
echo ""

# Summary checks
DB_OK=$(PGPASSWORD=***PASSWORD*** psql -h $DB_SERVER -U genie_admin -d tinker_genie -c "SELECT 1;" 2>/dev/null && echo "OK" || echo "FAIL")
API_OK=$(systemctl is-active --quiet tinker-api.service && echo "OK" || echo "FAIL")
REDIS_OK=$(redis-cli -a ***PASSWORD*** ping &>/dev/null && echo "OK" || echo "FAIL")

echo "✅ Database Connection: $DB_OK"
echo "✅ API Service: $API_OK" 
echo "✅ Redis Cache: $REDIS_OK"
echo ""
echo "🎯 WHAT'S MISSING?"
echo "=================="
echo ""

# Check for missing components
MISSING=()

if [ "$DB_OK" != "OK" ]; then
    MISSING+=("Database connection")
fi

if [ "$API_OK" != "OK" ]; then
    MISSING+=("API service")
fi

if [ "$REDIS_OK" != "OK" ]; then
    MISSING+=("Redis cache")
fi

if [ ! -f "$API_DIR/Controllers/ChatController.cs" ]; then
    MISSING+=("ChatController")
fi

if [ ! -f "$API_DIR/Controllers/AuthController.cs" ]; then
    MISSING+=("AuthController") 
fi

if [ ! -f "$API_DIR/Controllers/PreferencesController.cs" ]; then
    MISSING+=("PreferencesController")
fi

if [ ${#MISSING[@]} -eq 0 ]; then
    echo "🎊 ALL CORE COMPONENTS PRESENT!"
    echo ""
    echo "Ready to check:"
    echo "- Login authentication working?"
    echo "- Daily prompt delivery working?"
    echo "- Conversation persistence working?"
    echo "- User preferences working?"
    echo "- Day counter advancing?"
else
    echo "❌ Missing components:"
    for item in "${MISSING[@]}"; do
        echo "   - $item"
    done
fi

echo ""
echo "✅ SYSTEM CHECK COMPLETE"
echo "========================"
echo ""
