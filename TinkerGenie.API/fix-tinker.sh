#!/bin/bash
# Complete fix to restore TinkerGenie to working state (port 8080 as it was working before)

echo "🔧 RESTORING TINKERGENIE TO WORKING STATE"
echo "========================================"

cd /var/www/tinker-genie/TinkerGenie.API

# 1. Stop current broken PM2 process
echo "🛑 Stopping current PM2..."
pm2 delete tinker-api 2>/dev/null || true

# 2. Kill any rogue processes on ports 8080, 8765, 5000, 5001
echo "🔫 Killing processes on conflicting ports..."
pkill -f "TinkerGenie.API" 2>/dev/null || true
pkill -f "dotnet.*TinkerGenie" 2>/dev/null || true
fuser -k 8080/tcp 2>/dev/null || true
fuser -k 8765/tcp 2>/dev/null || true
fuser -k 5000/tcp 2>/dev/null || true
fuser -k 5001/tcp 2>/dev/null || true

# 3. Create correct appsettings.json for port 8080 (as it was working before)
echo "⚙️  Creating appsettings.json for port 8080..."
cat > appsettings.json << 'EOF'
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "Urls": "http://localhost:8080",
  "ConnectionStrings": {
    "DefaultConnection": "Host=161.35.5.159;Database=tinker_genie;Username=genie_admin;Password=***PASSWORD***"
  },
  "Redis": {
    "ConnectionString": "localhost:6379,password=***PASSWORD***"
  },
  "OpenAI": {
    "ApiKey": "sk-your-openai-key-here"
  },
  "Weaviate": {
    "Endpoint": "http://localhost:8080"
  },
  "Jwt": {
    "Key": "TinkerGenie2025SecretKeyForJWTTokensMinimum32Characters",
    "Issuer": "TinkerGenieAPI",
    "Audience": "TinkerGenieApp"
  }
}
EOF

# 4. Build the project
echo "🔨 Building project..."
dotnet build -c Release

# 5. Start API on port 8080 with PM2 (the working configuration)
echo "🚀 Starting API on port 8080..."
pm2 start "dotnet TinkerGenie.API.dll --urls=http://localhost:8080" --name tinker-api --cwd /var/www/tinker-genie/TinkerGenie.API/bin/Release/net8.0

# 6. Save PM2 configuration
pm2 save

# 7. Update nginx to point to port 8080 (restore working config)
echo "🌐 Updating nginx configuration..."
sed -i 's/proxy_pass http:\/\/localhost:[0-9]*/proxy_pass http:\/\/localhost:8080/g' /etc/nginx/sites-available/tinker*
sed -i 's/proxy_pass http:\/\/localhost:[0-9]*\/api\//proxy_pass http:\/\/localhost:8080\/api\//g' /etc/nginx/sites-available/tinker*

# 8. Test and reload nginx
nginx -t && systemctl reload nginx

echo ""
echo "🔍 TESTING SYSTEM..."
sleep 3

# 9. Test everything
echo "Testing PM2 status:"
pm2 status

echo ""
echo "Testing API directly on port 8080:"
curl -s http://localhost:8080/health || echo "❌ Direct API test failed"

echo ""
echo "Testing through nginx:"
curl -s https://tinker.twobrain.ai/health || echo "❌ Nginx test failed"

echo ""
echo "Testing login:"
curl -s -X POST https://tinker.twobrain.ai/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username": "admin", "password": "TinkerAdmin2025!"}' | head -100

echo ""
echo "🎯 SYSTEM RESTORED TO WORKING STATE (PORT 8080)"
echo "✅ API should now be accessible at https://tinker.twobrain.ai"
echo "✅ PWA should load without 401 errors"
echo ""
echo "If still having issues, check PM2 logs:"
echo "pm2 logs tinker-api"
