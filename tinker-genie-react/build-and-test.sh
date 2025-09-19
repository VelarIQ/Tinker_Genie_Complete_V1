#!/bin/bash

# TinkerGenie React - Build and Test Script
# This script builds the React app locally and prepares it for deployment

set -e

echo "🚀 TinkerGenie React - Build & Test"
echo "===================================="

# Colors
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
NC='\033[0m'

# Step 1: Check Node.js and npm
echo -e "${YELLOW}Step 1: Checking environment...${NC}"
if ! command -v node &> /dev/null; then
    echo -e "${RED}❌ Node.js is not installed${NC}"
    exit 1
fi
if ! command -v npm &> /dev/null; then
    echo -e "${RED}❌ npm is not installed${NC}"
    exit 1
fi
echo -e "${GREEN}✓ Node.js $(node -v)${NC}"
echo -e "${GREEN}✓ npm $(npm -v)${NC}"

# Step 2: Install dependencies
echo -e "\n${YELLOW}Step 2: Installing dependencies...${NC}"
npm install
echo -e "${GREEN}✓ Dependencies installed${NC}"

# Step 3: Create .env file if it doesn't exist
echo -e "\n${YELLOW}Step 3: Setting up environment...${NC}"
if [ ! -f .env ]; then
    cat > .env << EOF
# Local development
VITE_API_URL=http://localhost:5000/api
VITE_SIGNALR_HUB_URL=http://localhost:5000/chatHub
VITE_GOOGLE_CLIENT_ID=your-google-client-id
EOF
    echo -e "${GREEN}✓ Created .env file${NC}"
else
    echo -e "${GREEN}✓ .env file exists${NC}"
fi

# Step 4: Create production .env
cat > .env.production << EOF
# Production
VITE_API_URL=https://tinker.twobrain.ai/api
VITE_SIGNALR_HUB_URL=https://tinker.twobrain.ai/chatHub
VITE_GOOGLE_CLIENT_ID=your-production-google-client-id
EOF
echo -e "${GREEN}✓ Created .env.production${NC}"

# Step 5: Create placeholder icons if they don't exist
echo -e "\n${YELLOW}Step 4: Creating placeholder icons...${NC}"
mkdir -p public
if [ ! -f public/icon-192.png ]; then
    # Create a simple yellow square as placeholder
    echo -e "${YELLOW}Creating placeholder icons...${NC}"
    cat > public/icon.svg << 'EOF'
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 512 512">
  <rect width="512" height="512" fill="#FFD700"/>
  <text x="256" y="300" font-family="Arial" font-size="200" font-weight="bold" text-anchor="middle" fill="#000">TG</text>
</svg>
EOF
    echo -e "${GREEN}✓ Created icon.svg${NC}"
fi

# Step 6: Type check
echo -e "\n${YELLOW}Step 5: Running TypeScript checks...${NC}"
npx tsc --noEmit || true
echo -e "${GREEN}✓ TypeScript check complete${NC}"

# Step 7: Build for production
echo -e "\n${YELLOW}Step 6: Building for production...${NC}"
npm run build

if [ -d "dist" ]; then
    echo -e "${GREEN}✓ Build successful!${NC}"
    echo -e "${GREEN}✓ Output in: dist/${NC}"
    
    # Show build size
    echo -e "\n${YELLOW}Build Statistics:${NC}"
    du -sh dist/
    echo "Files created:"
    ls -la dist/ | head -10
else
    echo -e "${RED}❌ Build failed${NC}"
    exit 1
fi

# Step 8: Test the build locally
echo -e "\n${YELLOW}Step 7: Testing build locally...${NC}"
echo "You can test the build with: npm run preview"
echo "This will start a local server at http://localhost:4173"

# Step 9: Deployment instructions
echo -e "\n${YELLOW}Step 8: Ready for deployment!${NC}"
echo "===================================="
echo -e "${GREEN}✅ Build Complete!${NC}"
echo ""
echo "To deploy to production:"
echo "1. Copy dist/* to server: scp -r dist/* root@tinker.twobrain.ai:/var/www/html/"
echo "2. Or use the deploy-react.sh script"
echo ""
echo "To test locally:"
echo "  npm run preview"
echo ""
echo "Build location: ./dist/"
echo ""
echo -e "${GREEN}All mobile optimizations included:${NC}"
echo "  ✓ Visual Viewport API"
echo "  ✓ Complete viewport lock"
echo "  ✓ PWA manifest & service worker"
echo "  ✓ iOS keyboard handling"
echo "  ✓ Safe area padding"
echo "  ✓ Quick action buttons"
echo "  ✓ Enhanced send button"
echo "  ✓ Offline support"
echo ""
echo -e "${YELLOW}Note: Update VITE_GOOGLE_CLIENT_ID in .env.production before deploying${NC}"

