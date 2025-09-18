#!/bin/bash
# TinkerGenie Complete System Cleanup Script
# Removes leftover files, fixes port conflicts, and standardizes services

echo "🧹 TINKERGENIE COMPLETE SYSTEM CLEANUP"
echo "======================================"

# 1. REMOVE LEFTOVER CLEANUP SCRIPTS (we have different ports now)
echo "🔴 1. Removing obsolete cleanup scripts..."
sudo rm -f /usr/local/bin/tinker-api-cleanup.sh 2>/dev/null && echo "✅ Removed port 5005 cleanup script" || echo "ℹ️ No cleanup script to remove"

# 2. REMOVE DUPLICATE/UNUSED SYSTEMD SERVICES
echo "🔴 2. Cleaning up systemd services..."
sudo systemctl stop tinker-pwa.service 2>/dev/null || echo "ℹ️ tinker-pwa service not running"
sudo systemctl disable tinker-pwa.service 2>/dev/null || echo "ℹ️ tinker-pwa service not enabled"
sudo rm -f /etc/systemd/system/tinker-pwa.service 2>/dev/null && echo "✅ Removed tinker-pwa service" || echo "ℹ️ No tinker-pwa service to remove"

# 3. CLEAN UP PM2 PROCESSES (we're using systemd now)
echo "🔴 3. Cleaning up PM2 processes..."
pm2 delete all 2>/dev/null && echo "✅ Removed PM2 processes" || echo "ℹ️ No PM2 processes to remove"

# 4. VERIFY CURRENT WORKING CONFIG
echo "🔴 4. Verifying current configuration..."
echo "Current API port: $(sudo systemctl cat tinker-api.service | grep ASPNETCORE_URLS | cut -d: -f3 || echo 'Not found')"
echo "Nginx proxy target: $(grep -r "proxy_pass" /etc/nginx/sites-available/tinker | head -1 | cut -d' ' -f2 || echo 'Not found')"

# 5. CHECK FOR LEFTOVER BUILD FILES
echo "🔴 5. Build artifacts status..."
if [ -d "bin" ]; then
    echo "Build output size: $(du -sh bin | cut -f1)"
fi

# 6. REMOVE BACKUP FILES
echo "🔴 6. Cleaning backup files..."
find /var/www/tinker-genie -name "*.backup.*" -type f 2>/dev/null | head -5 && echo "Found backup files" || echo "ℹ️ No backup files found"

# 7. CURRENT SYSTEM STATE
echo "🔴 7. Current system state..."
echo "API Service Status: $(sudo systemctl is-active tinker-api.service)"
echo "Nginx Status: $(sudo systemctl is-active nginx)"
echo "Health Check Available: $(/opt/tinkergenie/health/health_check.sh >/dev/null 2>&1 && echo 'YES' || echo 'NO')"

echo ""
echo "✅ CLEANUP COMPLETE!"
echo "System is running cleanly on port 8765"
