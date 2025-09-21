#!/bin/bash

# TinkerGenie React Setup Script
# This script helps convert your existing frontend to the new React app

echo "🚀 TinkerGenie React Conversion Helper"
echo "======================================"

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

# Check if we're in the right directory
if [ ! -f "package.json" ]; then
    echo -e "${RED}Error: Not in the tinker-genie-react directory${NC}"
    echo "Please run this script from: /Users/leightonbingham/tinker-genie-clean/tinker-genie-react"
    exit 1
fi

echo -e "${GREEN}✓ Found React project${NC}"

# Function to create component from existing JSX
convert_component() {
    local source_file=$1
    local dest_file=$2
    
    echo "Converting $source_file to TypeScript..."
    
    # Check if source exists
    if [ ! -f "../Frontend-Examples/$source_file" ]; then
        echo -e "${YELLOW}Warning: $source_file not found in Frontend-Examples${NC}"
        return
    fi
    
    # Create destination directory if needed
    mkdir -p $(dirname "src/$dest_file")
    
    # Copy and convert (basic conversion, might need manual fixes)
    cp "../Frontend-Examples/$source_file" "src/$dest_file"
    
    # Add TypeScript imports at the top
    echo "import React from 'react';" | cat - "src/$dest_file" > temp && mv temp "src/$dest_file"
    
    echo -e "${GREEN}✓ Created src/$dest_file${NC}"
}

# Step 1: Install dependencies
echo ""
echo "Step 1: Installing dependencies..."
echo "--------------------------------"
npm install
if [ $? -eq 0 ]; then
    echo -e "${GREEN}✓ Dependencies installed${NC}"
else
    echo -e "${RED}✗ Failed to install dependencies${NC}"
    exit 1
fi

# Step 2: Create necessary directories
echo ""
echo "Step 2: Creating directory structure..."
echo "--------------------------------------"
mkdir -p src/components/{auth,chat,common,layout}
mkdir -p src/pages
mkdir -p src/types
mkdir -p public
echo -e "${GREEN}✓ Directory structure created${NC}"

# Step 3: Convert existing components
echo ""
echo "Step 3: Converting existing components..."
echo "----------------------------------------"
convert_component "ChatApp.jsx" "components/chat/ChatApp.tsx"
convert_component "ChatWindow.jsx" "components/chat/ChatWindow.tsx"
convert_component "Sidebar.jsx" "components/layout/Sidebar.tsx"

# Step 4: Copy CSS files
echo ""
echo "Step 4: Copying styles..."
echo "------------------------"
if [ -f "../Frontend-Examples/ChatApp.css" ]; then
    cp ../Frontend-Examples/*.css src/components/
    echo -e "${GREEN}✓ Styles copied${NC}"
else
    echo -e "${YELLOW}⚠ No CSS files found to copy${NC}"
fi

# Step 5: Create environment file
echo ""
echo "Step 5: Creating environment file..."
echo "-----------------------------------"
if [ ! -f ".env" ]; then
    cat > .env << EOL
VITE_API_URL=http://localhost:5000/api
VITE_SIGNALR_HUB_URL=http://localhost:5000/chatHub
VITE_GOOGLE_CLIENT_ID=your-google-client-id-here
VITE_APP_NAME=TinkerGenie Leadership
EOL
    echo -e "${GREEN}✓ Created .env file${NC}"
    echo -e "${YELLOW}⚠ Remember to update your Google Client ID in .env${NC}"
else
    echo -e "${YELLOW}⚠ .env file already exists${NC}"
fi

# Step 6: Create missing components
echo ""
echo "Step 6: Creating missing components..."
echo "-------------------------------------"

# Create LoadingSpinner
cat > src/components/common/LoadingSpinner.tsx << 'EOL'
import React from 'react';

interface LoadingSpinnerProps {
  size?: 'small' | 'medium' | 'large';
}

export default function LoadingSpinner({ size = 'medium' }: LoadingSpinnerProps) {
  const sizeClasses = {
    small: 'w-4 h-4',
    medium: 'w-8 h-8',
    large: 'w-12 h-12',
  };
  
  return (
    <div className="flex items-center justify-center">
      <div className={`loading-spinner ${sizeClasses[size]}`} />
    </div>
  );
}
EOL
echo -e "${GREEN}✓ Created LoadingSpinner component${NC}"

# Create PrivateRoute
cat > src/components/auth/PrivateRoute.tsx << 'EOL'
import { Navigate, Outlet } from 'react-router-dom';
import { useAppSelector } from '../../hooks/redux';

export default function PrivateRoute() {
  const { isAuthenticated } = useAppSelector((state) => state.auth);
  
  return isAuthenticated ? <Outlet /> : <Navigate to="/login" replace />;
}
EOL
echo -e "${GREEN}✓ Created PrivateRoute component${NC}"

# Create basic LoginPage
cat > src/pages/LoginPage.tsx << 'EOL'
import React, { useState } from 'react';
import { useAppDispatch } from '../hooks/redux';
import { login } from '../store/authSlice';

export default function LoginPage() {
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const dispatch = useAppDispatch();

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    dispatch(login({ email, password }));
  };

  return (
    <div className="min-h-screen flex items-center justify-center bg-gray-50">
      <div className="card max-w-md w-full">
        <h1 className="text-2xl font-bold mb-6 text-center gradient-text">
          Welcome to TinkerGenie
        </h1>
        <form onSubmit={handleSubmit} className="space-y-4">
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">
              Email
            </label>
            <input
              type="email"
              placeholder="Enter your email"
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              className="input-field"
              required
            />
          </div>
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">
              Password
            </label>
            <input
              type="password"
              placeholder="Enter your password"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              className="input-field"
              required
            />
          </div>
          <button type="submit" className="btn-primary w-full">
            Login
          </button>
        </form>
      </div>
    </div>
  );
}
EOL
echo -e "${GREEN}✓ Created LoginPage${NC}"

# Create basic ChatPage
cat > src/pages/ChatPage.tsx << 'EOL'
import React from 'react';

export default function ChatPage() {
  return (
    <div className="flex h-screen bg-gray-50">
      <div className="flex-1 flex items-center justify-center">
        <h1 className="text-2xl font-bold gradient-text">Chat Page - Ready for Integration</h1>
      </div>
    </div>
  );
}
EOL
echo -e "${GREEN}✓ Created ChatPage${NC}"

# Create placeholder pages
cat > src/pages/DashboardPage.tsx << 'EOL'
import React from 'react';

export default function DashboardPage() {
  return (
    <div className="p-8">
      <h1 className="text-2xl font-bold mb-4">Dashboard</h1>
      <p>Dashboard coming soon...</p>
    </div>
  );
}
EOL
echo -e "${GREEN}✓ Created DashboardPage${NC}"

cat > src/pages/SettingsPage.tsx << 'EOL'
import React from 'react';

export default function SettingsPage() {
  return (
    <div className="p-8">
      <h1 className="text-2xl font-bold mb-4">Settings</h1>
      <p>Settings coming soon...</p>
    </div>
  );
}
EOL
echo -e "${GREEN}✓ Created SettingsPage${NC}"

# Create SignalR service
cat > src/services/signalRService.ts << 'EOL'
import * as signalR from '@microsoft/signalr';

class SignalRService {
  private connection: signalR.HubConnection | null = null;

  async connect(token: string): Promise<signalR.HubConnection> {
    const hubUrl = import.meta.env.VITE_SIGNALR_HUB_URL || 'http://localhost:5000/chatHub';
    
    this.connection = new signalR.HubConnectionBuilder()
      .withUrl(hubUrl, {
        accessTokenFactory: () => token,
      })
      .withAutomaticReconnect()
      .build();

    await this.connection.start();
    console.log('SignalR Connected');
    return this.connection;
  }

  disconnect() {
    if (this.connection) {
      this.connection.stop();
      console.log('SignalR Disconnected');
    }
  }

  onReceiveMessage(callback: (user: string, message: string) => void) {
    this.connection?.on('ReceiveMessage', callback);
  }

  async sendMessage(message: string) {
    if (this.connection) {
      await this.connection.invoke('SendMessage', message);
    }
  }
}

export const signalRService = new SignalRService();
EOL
echo -e "${GREEN}✓ Created SignalR service${NC}"

# Create conversation service
cat > src/services/conversationService.ts << 'EOL'
import { apiService } from './apiService';
import { Conversation } from '../store/conversationSlice';

class ConversationService {
  async getConversations(page = 1, limit = 20) {
    return apiService.get<{
      conversations: Conversation[];
      hasMore: boolean;
      page: number;
    }>('/conversations', { page, limit });
  }

  async getRecentConversations() {
    return apiService.get<Conversation[]>('/conversations/recent');
  }

  async getConversation(id: string) {
    return apiService.get<Conversation>(`/conversations/${id}`);
  }

  async deleteConversation(id: string) {
    return apiService.delete(`/conversations/${id}`);
  }

  async updateConversationTitle(id: string, title: string) {
    return apiService.put<Conversation>(`/conversations/${id}`, { title });
  }
}

export const conversationService = new ConversationService();
EOL
echo -e "${GREEN}✓ Created conversation service${NC}"

# Create index.html if it doesn't exist
if [ ! -f "index.html" ]; then
    cat > index.html << 'EOL'
<!DOCTYPE html>
<html lang="en">
  <head>
    <meta charset="UTF-8" />
    <link rel="icon" type="image/svg+xml" href="/vite.svg" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0" />
    <title>TinkerGenie Leadership</title>
  </head>
  <body>
    <div id="root"></div>
    <script type="module" src="/src/main.tsx"></script>
  </body>
</html>
EOL
    echo -e "${GREEN}✓ Created index.html${NC}"
fi

# Step 7: Create additional config files
echo ""
echo "Step 7: Creating configuration files..."
echo "--------------------------------------"

# Create postcss.config.js
cat > postcss.config.js << 'EOL'
export default {
  plugins: {
    tailwindcss: {},
    autoprefixer: {},
  },
}
EOL
echo -e "${GREEN}✓ Created postcss.config.js${NC}"

# Create tsconfig.node.json
cat > tsconfig.node.json << 'EOL'
{
  "compilerOptions": {
    "composite": true,
    "skipLibCheck": true,
    "module": "ESNext",
    "moduleResolution": "bundler",
    "allowSyntheticDefaultImports": true
  },
  "include": ["vite.config.ts"]
}
EOL
echo -e "${GREEN}✓ Created tsconfig.node.json${NC}"

# Final summary
echo ""
echo "======================================"
echo -e "${GREEN}✅ React App Setup Complete!${NC}"
echo "======================================"
echo ""
echo "Next steps:"
echo "-----------"
echo "1. Update your .env file with your Google Client ID"
echo "2. Make sure your .NET API is running on port 5000"
echo "3. Run: npm run dev"
echo "4. Open: http://localhost:3000"
echo ""
echo "Manual tasks needed:"
echo "-------------------"
echo "• Review and fix TypeScript errors in converted components"
echo "• Update API endpoints to match your backend"
echo "• Test authentication flow"
echo "• Integrate SignalR for real-time features"
echo ""
echo -e "${GREEN}Happy coding! 🚀${NC}"
