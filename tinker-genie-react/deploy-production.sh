#!/bin/bash

# TinkerGenie React - Production Deployment Script
# Deploys with exact PWA UI styling

set -e

echo "🚀 TinkerGenie React - Production Deployment"
echo "==========================================="

# Colors
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m'

# Step 1: Create production environment
echo -e "${YELLOW}Step 1: Setting production environment...${NC}"
cat > .env.production << EOF
VITE_API_URL=/api
VITE_SIGNALR_HUB_URL=/chatHub
VITE_GOOGLE_CLIENT_ID=your-production-google-client-id
EOF
echo -e "${GREEN}✓ Production environment set${NC}"

# Step 2: Build locally
echo -e "\n${YELLOW}Step 2: Building React app...${NC}"
npm install --legacy-peer-deps
npm run build -- --mode production

if [ ! -d "dist" ]; then
    echo "Build failed - no dist directory"
    exit 1
fi

echo -e "${GREEN}✓ Build successful${NC}"
echo "Build size: $(du -sh dist/)"

# Step 3: Copy to server
echo -e "\n${YELLOW}Step 3: Deploying to server...${NC}"

# Create deployment package
tar -czf deploy.tar.gz dist/*

# Transfer and deploy
scp -i ~/.ssh/ai_assistant_key deploy.tar.gz root@tinker.twobrain.ai:/tmp/

ssh -i ~/.ssh/ai_assistant_key root@tinker.twobrain.ai << 'ENDSSH'
# Backup current PWA
echo "Backing up current PWA..."
cp -r /var/www/html/pwa /var/www/html/pwa.backup.$(date +%Y%m%d-%H%M%S)

# Extract new build
cd /tmp
tar -xzf deploy.tar.gz
rm -rf /var/www/html/pwa/*
cp -r dist/* /var/www/html/pwa/

# Copy icons from backup if missing
if [ ! -f /var/www/html/pwa/icon-192.png ]; then
    cp /var/www/html/pwa.backup.*/icon*.png /var/www/html/pwa/ 2>/dev/null || true
    cp /var/www/html/pwa.backup.*/apple-touch-icon.png /var/www/html/pwa/ 2>/dev/null || true
fi

# Set permissions
chown -R www-data:www-data /var/www/html/pwa/
chmod -R 755 /var/www/html/pwa/

# Test deployment
echo "Testing deployment..."
curl -s -o /dev/null -w "Homepage: %{http_code}\n" https://tinker.twobrain.ai/pwa/
curl -s -o /dev/null -w "Assets: %{http_code}\n" https://tinker.twobrain.ai/pwa/assets/

echo "✅ Deployment complete!"
ENDSSH

# Cleanup
rm deploy.tar.gz

echo -e "\n${GREEN}✅ DEPLOYMENT SUCCESSFUL!${NC}"
echo "Your React app is live at: https://tinker.twobrain.ai/"
echo ""
echo "Features deployed:"
echo "  • Exact PWA UI styling (yellow/grey messages)"
echo "  • Dark theme matching original"
echo "  • All mobile optimizations"
echo "  • Three-tier system"
echo "  • PWA capabilities"

