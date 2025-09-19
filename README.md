# TinkerGenie Complete v1

🚀 **Complete TinkerGenie Leadership Development Platform**

## 📋 Overview

TinkerGenie is a comprehensive leadership coaching system with three-tier support:
- **Daily Prompts**: Reflection-based leadership development
- **Burning Fires**: Urgent crisis management with AI-powered guidance
- **Other Tinker Level Fires**: General leadership challenges

## 🏗️ Architecture

### Technology Stack
- **Frontend**: 
  - PWA (Progressive Web App) - Currently deployed
  - React with TypeScript - Alternative modern frontend
- **Backend**: .NET Core 8.0 API with SignalR
- **Database**: PostgreSQL
- **Cache**: Redis
- **Vector Database**: Weaviate
- **Web Server**: Nginx (load balancer for 3 API instances)

## 📁 Repository Structure

```
Tinker-Genie-Complete-v1/
├── pwa/                    # Production PWA (currently deployed)
├── tinker-genie-react/     # React app with TypeScript
├── TinkerGenie.API/        # .NET Core backend API
├── configs/                # Nginx & systemd configurations
└── README.md              # This file
```

## 🔐 Default Credentials

### Regular Users
**Password**: `TinkerGenie2025!`
- `leighton`
- `chris`
- `testuser`

### Admin User
**Password**: `TinkerAdmin2025!`
- `admin`

## 🚀 Quick Start

### Prerequisites
- .NET Core 8.0 SDK
- Node.js 18+
- PostgreSQL 14+
- Redis
- Weaviate
- Nginx

### Backend Setup
```bash
cd TinkerGenie.API
dotnet restore
dotnet build
dotnet run
```

### Frontend Setup (PWA)
The PWA is ready to deploy - just serve the `pwa/` directory with any web server.

### Frontend Setup (React)
```bash
cd tinker-genie-react
npm install
npm run build
# Deploy dist/ folder to web server
```

## 🔧 Configuration

### API Configuration
Edit `TinkerGenie.API/appsettings.json`:
- PostgreSQL connection string
- Redis connection
- Weaviate endpoint
- JWT settings

### Nginx Configuration
See `configs/nginx.conf` for load balancer setup

## 📝 Features

- ✅ Multi-user authentication with JWT
- ✅ Real-time chat with SignalR
- ✅ Daily leadership prompts
- ✅ Crisis management (Burning Fires)
- ✅ Knowledge base integration (Weaviate)
- ✅ Mobile optimized PWA
- ✅ Dark/Light theme support
- ✅ Conversation history
- ✅ User preferences

## 🌐 Production URL

Currently deployed at: https://tinker.twobrain.ai

## 📄 License

Proprietary - TinkerGenie © 2025

## 👥 Support

For support, contact: support@tinkergenie.com
