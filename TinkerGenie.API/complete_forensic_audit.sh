#!/bin/bash

echo "🔍 COMPLETE TINKERGENIE FORENSIC AUDIT"
echo "======================================"
echo "Leaving no rock unturned..."
echo ""

# Configuration
DB_SERVER="161.35.5.159"
API_SERVER="24.144.119.69"

echo "🖥️ SYSTEM INFORMATION"
echo "===================="
echo "Hostname: $(hostname)"
echo "OS: $(lsb_release -a 2>/dev/null | grep Description | cut -d: -f2 | xargs)"
echo "Kernel: $(uname -r)"
echo "Uptime: $(uptime -p)"
echo "Load: $(uptime | awk -F'load average:' '{print $2}')"
echo "Memory: $(free -h | grep Mem | awk '{print $3 "/" $2}')"
echo "Disk: $(df -h / | tail -1 | awk '{print $3 "/" $2 " (" $5 " used)"}')"
echo ""

echo "👥 USERS & PERMISSIONS"
echo "======================"
echo "Current user: $(whoami)"
echo "Groups: $(groups)"
echo ""
echo "All users with shell access:"
grep -E '/bin/(bash|sh)$' /etc/passwd
echo ""
echo "Recent logins:"
last -10
echo ""

echo "🌐 NETWORK & PORTS"
echo "=================="
echo "Public IP: $(curl -s ifconfig.me 2>/dev/null || echo 'Unable to determine')"
echo "Local IP: $(hostname -I | awk '{print $1}')"
echo ""
echo "All listening ports:"
netstat -tulpn | grep LISTEN | sort -k4
echo ""
echo "Active connections:"
netstat -tupn | grep ESTABLISHED | head -10
echo ""

echo "🔧 RUNNING SERVICES"
echo "==================="
echo "All active systemd services:"
systemctl list-units --state=active --type=service | grep -E "(tinker|nginx|postgresql|redis|docker)" || echo "No TinkerGenie related services found"
echo ""
echo "All systemd services (including failed):"
systemctl list-units --type=service | grep -E "(tinker|nginx|postgresql|redis|docker|api)" || echo "No related services found"
echo ""
echo "Service status details:"
for service in tinker-api nginx postgresql redis-server docker; do
    echo "--- $service ---"
    systemctl is-active $service 2>/dev/null && echo "✅ ACTIVE" || echo "❌ INACTIVE"
    systemctl is-enabled $service 2>/dev/null && echo "✅ ENABLED" || echo "❌ DISABLED"
done
echo ""

echo "🐳 DOCKER CONTAINERS"
echo "===================="
if command -v docker &> /dev/null; then
    echo "Docker version: $(docker --version 2>/dev/null || echo 'Not installed')"
    echo "Running containers:"
    docker ps -a 2>/dev/null || echo "No containers or docker not accessible"
    echo ""
    echo "Docker images:"
    docker images 2>/dev/null || echo "No images or docker not accessible"
else
    echo "Docker not installed"
fi
echo ""

echo "📁 COMPLETE FILE SYSTEM AUDIT"
echo "============================="
echo ""
echo "=== /var/www STRUCTURE ==="
find /var/www -type f -name "*.cs" -o -name "*.js" -o -name "*.html" -o -name "*.json" -o -name "*.sh" 2>/dev/null | sort
echo ""
echo "=== /etc CONFIGURATIONS ==="
find /etc -name "*tinker*" -o -name "*nginx*" -o -name "*systemd*" 2>/dev/null | grep -E "(tinker|api)" | sort
echo ""
echo "=== /home DIRECTORIES ==="
find /home -name "*.sh" -o -name "*.json" -o -name "*.md" -o -name "deploy*" 2>/dev/null | sort
echo ""
echo "=== ROOT DIRECTORY ==="
find /root -maxdepth 2 -name "*.sh" -o -name "*.json" -o -name "*.md" -o -name "deploy*" 2>/dev/null | sort
echo ""

echo "🗃️ DATABASE COMPLETE AUDIT"
echo "=========================="
echo "Testing database connection..."
PGPASSWORD=***PASSWORD*** psql -h $DB_SERVER -U genie_admin -d tinker_genie << 'EOSQL'

-- Server information
SELECT version() as postgresql_version;
SELECT current_database(), current_user, inet_server_addr(), inet_server_port();

-- All databases
\l

-- All tables in current database
SELECT 
    schemaname,
    tablename,
    tableowner,
    tablespace,
    hasindexes,
    hasrules,
    hastriggers
FROM pg_tables 
WHERE schemaname = 'public'
ORDER BY tablename;

-- Table sizes
SELECT 
    schemaname,
    tablename,
    pg_size_pretty(pg_total_relation_size(schemaname||'.'||tablename)) as size
FROM pg_tables 
WHERE schemaname = 'public'
ORDER BY pg_total_relation_size(schemaname||'.'||tablename) DESC;

-- Record counts for all tables
DO $$
DECLARE
    table_name TEXT;
    row_count INTEGER;
BEGIN
    FOR table_name IN 
        SELECT tablename FROM pg_tables WHERE schemaname = 'public'
    LOOP
        EXECUTE 'SELECT COUNT(*) FROM ' || table_name INTO row_count;
        RAISE NOTICE 'Table %: % rows', table_name, row_count;
    END LOOP;
END $$;

-- Users and data samples
SELECT 'USER DATA SAMPLE:' as info;
SELECT user_id, name, first_name, business_name, current_day, is_active 
FROM user_data 
ORDER BY created_at DESC 
LIMIT 5;

-- Prompts sample
SELECT 'LEADERSHIP PROMPTS SAMPLE:' as info;
SELECT day_number, prompt_title, LEFT(prompt_text, 100) as prompt_preview
FROM leadership_daily_prompts 
ORDER BY day_number 
LIMIT 5;

-- Recent conversations
SELECT 'RECENT CONVERSATIONS SAMPLE:' as info;
SELECT 
    LEFT(message_text, 50) as message_preview,
    sender,
    created_at
FROM conversation_messages 
ORDER BY created_at DESC 
LIMIT 5;

-- Database connections
SELECT 
    application_name,
    client_addr,
    state,
    query_start,
    LEFT(query, 50) as current_query
FROM pg_stat_activity 
WHERE state = 'active';

EOSQL

echo ""
echo "☁️ REDIS COMPLETE AUDIT"
echo "======================="
if command -v redis-cli &> /dev/null; then
    echo "Redis server info:"
    redis-cli -a ***PASSWORD*** INFO server 2>/dev/null | head -10
    echo ""
    echo "Redis memory info:"
    redis-cli -a ***PASSWORD*** INFO memory 2>/dev/null | grep -E "(used_memory|maxmemory)"
    echo ""
    echo "Redis keys:"
    redis-cli -a ***PASSWORD*** KEYS "*" 2>/dev/null | head -20
    echo ""
    echo "Redis config:"
    redis-cli -a ***PASSWORD*** CONFIG GET "*password*" 2>/dev/null
else
    echo "Redis CLI not available"
fi
echo ""

echo "📄 CONFIGURATION FILES AUDIT"
echo "============================"
echo ""
echo "=== NGINX CONFIGURATION ==="
if [ -d "/etc/nginx" ]; then
    echo "Nginx sites enabled:"
    ls -la /etc/nginx/sites-enabled/ 2>/dev/null || echo "No sites enabled"
    echo ""
    echo "Nginx configuration files:"
    find /etc/nginx -name "*.conf" 2>/dev/null
    echo ""
    if [ -f "/etc/nginx/sites-enabled/default" ]; then
        echo "Default nginx site:"
        cat /etc/nginx/sites-enabled/default | head -30
    fi
else
    echo "Nginx not installed or configured"
fi
echo ""

echo "=== SYSTEMD SERVICE FILES ==="
echo "TinkerGenie service file:"
if [ -f "/etc/systemd/system/tinker-api.service" ]; then
    cat /etc/systemd/system/tinker-api.service
else
    echo "No tinker-api.service file found"
fi
echo ""

echo "=== CRON JOBS ==="
echo "System crontabs:"
crontab -l 2>/dev/null || echo "No user crontab"
echo ""
echo "System-wide cron:"
ls -la /etc/cron.* 2>/dev/null | grep -v "^total"
echo ""

echo "📊 API APPLICATION COMPLETE AUDIT"
echo "================================="
API_DIR="/var/www/tinker-genie/TinkerGenie.API"
echo ""
echo "=== PROJECT STRUCTURE ==="
if [ -d "$API_DIR" ]; then
    echo "TinkerGenie API directory structure:"
    find "$API_DIR" -type f | sort
    echo ""
    
    echo "=== PROJECT FILES SIZES ==="
    find "$API_DIR" -type f -exec ls -lh {} \; | awk '{print $5, $9}' | sort
    echo ""
    
    echo "=== CSPROJ FILE ==="
    if [ -f "$API_DIR/TinkerGenie.API.csproj" ]; then
        cat "$API_DIR/TinkerGenie.API.csproj"
    else
        echo "No .csproj file found"
    fi
    echo ""
    
    echo "=== APPSETTINGS.JSON ==="
    if [ -f "$API_DIR/appsettings.json" ]; then
        echo "File exists, size: $(wc -c < $API_DIR/appsettings.json) bytes"
        # Show structure without sensitive data
        cat "$API_DIR/appsettings.json" | jq 'del(.OpenAI.ApiKey) | del(.ConnectionStrings.DefaultConnection)' 2>/dev/null || cat "$API_DIR/appsettings.json"
    else
        echo "No appsettings.json found"
    fi
    echo ""
    
    echo "=== PROGRAM.CS ==="
    if [ -f "$API_DIR/Program.cs" ]; then
        echo "File size: $(wc -c < $API_DIR/Program.cs) bytes"
        echo "First 50 lines:"
        head -50 "$API_DIR/Program.cs"
    else
        echo "No Program.cs found"
    fi
    echo ""
    
else
    echo "TinkerGenie API directory not found at $API_DIR"
fi

echo "=== ALL CONTROLLERS DETAILED ANALYSIS ==="
CONTROLLERS_DIR="$API_DIR/Controllers"
if [ -d "$CONTROLLERS_DIR" ]; then
    for controller in "$CONTROLLERS_DIR"/*.cs; do
        if [ -f "$controller" ]; then
            filename=$(basename "$controller")
            echo ""
            echo "--- $filename ---"
            echo "Size: $(wc -c < $controller) bytes"
            echo "Lines: $(wc -l < $controller) lines"
            echo "HTTP endpoints:"
            grep -E "\[Http(Get|Post|Put|Patch|Delete)" "$controller" || echo "No HTTP endpoints found"
            echo "Dependencies:"
            grep -E "^using " "$controller" | head -10
            echo "Class definition:"
            grep -E "class.*Controller" "$controller"
            echo "Constructor:"
            grep -A 10 "public.*Controller(" "$controller" | head -15
            echo "First method:"
            grep -A 5 -E "public.*Task.*Action" "$controller" | head -10
        fi
    done
else
    echo "Controllers directory not found"
fi
echo ""

echo "=== ALL MODELS ANALYSIS ==="
MODELS_DIR="$API_DIR/Models"
if [ -d "$MODELS_DIR" ]; then
    for model in "$MODELS_DIR"/*.cs; do
        if [ -f "$model" ]; then
            filename=$(basename "$model")
            echo ""
            echo "--- $filename ---"
            echo "Size: $(wc -c < $model) bytes"
            cat "$model"
        fi
    done
else
    echo "Models directory not found"
fi
echo ""

echo "🌐 WEB FRONTEND COMPLETE AUDIT"
echo "=============================="
echo ""
echo "=== /var/www/html ==="
if [ -d "/var/www/html" ]; then
    echo "HTML directory contents:"
    ls -la /var/www/html/
    echo ""
    for file in /var/www/html/*.html; do
        if [ -f "$file" ]; then
            filename=$(basename "$file")
            echo "--- $filename ---"
            echo "Size: $(wc -c < $file) bytes"
            echo "First 30 lines:"
            head -30 "$file"
            echo ""
        fi
    done
    
    for file in /var/www/html/*.js; do
        if [ -f "$file" ]; then
            filename=$(basename "$file")
            echo "--- $filename ---"
            echo "Size: $(wc -c < $file) bytes"
            echo "First 30 lines:"
            head -30 "$file"
            echo ""
        fi
    done
else
    echo "/var/www/html not found"
fi

echo "=== OTHER WEB DIRECTORIES ==="
find /var/www -name "index.html" -o -name "manifest.json" -o -name "*.js" 2>/dev/null | while read file; do
    echo "Found: $file ($(wc -c < $file) bytes)"
done
echo ""

echo "🔐 SECURITY & CREDENTIALS AUDIT"
echo "==============================="
echo ""
echo "=== FIREWALL STATUS ==="
ufw status 2>/dev/null || echo "UFW not installed"
echo ""
echo "=== SSH CONFIGURATION ==="
grep -E "^(Port|PermitRootLogin|PasswordAuthentication)" /etc/ssh/sshd_config 2>/dev/null || echo "SSH config not accessible"
echo ""
echo "=== SSL CERTIFICATES ==="
find /etc -name "*.crt" -o -name "*.key" -o -name "*.pem" 2>/dev/null | head -10
echo ""

echo "🔄 PROCESSES & RESOURCES"
echo "======================="
echo "Top processes by CPU:"
ps aux --sort=-%cpu | head -10
echo ""
echo "Top processes by memory:"
ps aux --sort=-%mem | head -10
echo ""
echo ".NET processes:"
ps aux | grep -E "(dotnet|TinkerGenie)" | grep -v grep
echo ""

echo "📝 LOG FILES AUDIT"
echo "=================="
echo ""
echo "=== SYSTEMD LOGS (TINKER API) ==="
echo "Last 20 lines of tinker-api service logs:"
sudo journalctl -u tinker-api.service -n 20 --no-pager 2>/dev/null || echo "No tinker-api logs"
echo ""
echo "=== NGINX LOGS ==="
if [ -f "/var/log/nginx/access.log" ]; then
    echo "Last 10 nginx access log entries:"
    tail -10 /var/log/nginx/access.log 2>/dev/null
fi
if [ -f "/var/log/nginx/error.log" ]; then
    echo "Last 10 nginx error log entries:"
    tail -10 /var/log/nginx/error.log 2>/dev/null
fi
echo ""

echo "🧪 API ENDPOINTS COMPREHENSIVE TESTING"
echo "======================================"
echo ""
echo "=== HEALTH CHECKS ==="
for endpoint in "/health" "/api/health" "/status" "/ping"; do
    echo "Testing $endpoint:"
    curl -s -o /dev/null -w "Status: %{http_code}, Time: %{time_total}s\n" "http://localhost:5000$endpoint" 2>/dev/null || echo "Failed to connect"
done
echo ""

echo "=== AUTHENTICATION TESTING ==="
echo "Testing login endpoint:"
LOGIN_RESPONSE=$(curl -s -X POST http://localhost:5000/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"email":"chris@twobrainbusiness.com","password":"TwoBrainOwner2025!"}' 2>/dev/null)

echo "Login response status: $(echo "$LOGIN_RESPONSE" | jq -r '.success' 2>/dev/null || echo 'Unable to parse')"
echo "Login response size: ${#LOGIN_RESPONSE} characters"

if [ ${#LOGIN_RESPONSE} -gt 10 ]; then
    TOKEN=$(echo "$LOGIN_RESPONSE" | jq -r '.token' 2>/dev/null)
    if [ "$TOKEN" != "null" ] && [ ! -z "$TOKEN" ]; then
        echo "✅ Token received"
        
        echo ""
        echo "=== AUTHENTICATED ENDPOINTS ==="
        for endpoint in "/api/chat" "/api/preferences/test-user" "/api/curriculum/today/test-user"; do
            echo "Testing $endpoint with auth:"
            curl -s -o /dev/null -w "Status: %{http_code}, Time: %{time_total}s\n" \
              -H "Authorization: Bearer $TOKEN" \
              "http://localhost:5000$endpoint" 2>/dev/null || echo "Failed"
        done
    else
        echo "❌ No valid token received"
    fi
else
    echo "❌ Login failed or no response"
fi
echo ""

echo "🏁 AUDIT SUMMARY & DISCREPANCIES"
echo "================================="
echo ""
echo "FILES THAT SHOULD BE LARGER THAN THEY ARE:"
if [ -f "$API_DIR/Controllers/ChatController.cs" ]; then
    CHAT_SIZE=$(wc -c < "$API_DIR/Controllers/ChatController.cs")
    if [ $CHAT_SIZE -lt 10000 ]; then
        echo "❌ ChatController.cs: $CHAT_SIZE bytes (should be 15000+ bytes)"
    else
        echo "✅ ChatController.cs: $CHAT_SIZE bytes (adequate)"
    fi
fi

if [ -f "$API_DIR/Controllers/PreferencesController.cs" ]; then
    PREF_SIZE=$(wc -c < "$API_DIR/Controllers/PreferencesController.cs")
    if [ $PREF_SIZE -lt 3000 ]; then
        echo "❌ PreferencesController.cs: $PREF_SIZE bytes (should be 5000+ bytes)"
    else
        echo "✅ PreferencesController.cs: $PREF_SIZE bytes (adequate)"
    fi
fi
echo ""

echo "MISSING CRITICAL FILES:"
CRITICAL_FILES=(
    "/var/www/tinker-genie/TinkerGenie.API/appsettings.json"
    "/var/www/tinker-genie/TinkerGenie.API/google-service-account.json"
    "/var/www/html/index.html"
    "/var/www/html/manifest.json"
)

for file in "${CRITICAL_FILES[@]}"; do
    if [ ! -f "$file" ]; then
        echo "❌ Missing: $file"
    else
        echo "✅ Present: $file"
    fi
done
echo ""

echo "🎯 WHAT NEEDS INVESTIGATION:"
echo "============================"
echo "Based on this audit, we need to search chat history for:"
echo "1. Why ChatController is smaller than expected"
echo "2. Complete PreferencesController implementation"
echo "3. PWA frontend files location and structure"
echo "4. Google Drive service account setup"
echo "5. Weaviate integration status"
echo "6. Redis caching implementation"
echo "7. Daily prompt delivery system"
echo "8. User authentication flow"
echo "9. Conversation persistence implementation"
echo "10. Chris Cooper coaching style prompts"
echo ""

echo "✅ COMPLETE FORENSIC AUDIT FINISHED"
echo "==================================="
echo "All systems, files, configurations, and services audited."
echo "Ready for chat history analysis to determine what should be restored."
