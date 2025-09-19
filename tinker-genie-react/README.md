# TinkerGenie React Frontend

A modern React frontend for the TinkerGenie Leadership Coaching platform, built with TypeScript, Redux Toolkit, TailwindCSS, and real-time SignalR integration.

## 🚀 Features

- **Modern Tech Stack**: React 18, TypeScript, Vite, Redux Toolkit
- **Real-time Chat**: SignalR integration for live messaging
- **Authentication**: JWT-based auth with Google OAuth support
- **State Management**: Redux Toolkit with async thunks
- **Styling**: TailwindCSS with custom design system
- **Data Fetching**: React Query for efficient server state management
- **Type Safety**: Full TypeScript support
- **Responsive Design**: Mobile-first approach
- **PWA Ready**: Offline support and installable

## 📦 Installation

### Prerequisites

- Node.js 18+ and npm/yarn
- Your .NET API running on `http://localhost:5000`

### Setup Steps

1. **Install dependencies:**
```bash
cd tinker-genie-react
npm install
```

2. **Create environment variables:**
Create a `.env` file in the root directory:
```env
VITE_API_URL=http://localhost:5000/api
VITE_SIGNALR_HUB_URL=http://localhost:5000/chatHub
VITE_GOOGLE_CLIENT_ID=your-google-client-id
VITE_APP_NAME=TinkerGenie
```

3. **Start the development server:**
```bash
npm run dev
```

The app will be available at `http://localhost:3000`

## 🏗️ Project Structure

```
tinker-genie-react/
├── src/
│   ├── components/        # Reusable UI components
│   │   ├── auth/          # Authentication components
│   │   ├── chat/          # Chat interface components
│   │   ├── common/        # Common UI elements
│   │   └── layout/        # Layout components
│   ├── pages/            # Page components
│   │   ├── LoginPage.tsx
│   │   ├── ChatPage.tsx
│   │   ├── DashboardPage.tsx
│   │   └── SettingsPage.tsx
│   ├── services/         # API services
│   │   ├── apiService.ts
│   │   ├── authService.ts
│   │   ├── chatService.ts
│   │   └── signalRService.ts
│   ├── store/           # Redux store and slices
│   │   ├── index.ts
│   │   ├── authSlice.ts
│   │   ├── chatSlice.ts
│   │   └── conversationSlice.ts
│   ├── hooks/           # Custom React hooks
│   ├── utils/           # Utility functions
│   ├── types/           # TypeScript type definitions
│   ├── App.tsx          # Main app component
│   ├── main.tsx         # App entry point
│   └── index.css        # Global styles
├── public/              # Static assets
├── package.json
├── vite.config.ts       # Vite configuration
├── tsconfig.json        # TypeScript configuration
└── tailwind.config.js   # TailwindCSS configuration
```

## 🛠️ Available Scripts

- `npm run dev` - Start development server
- `npm run build` - Build for production
- `npm run preview` - Preview production build
- `npm run lint` - Run ESLint
- `npm run type-check` - Run TypeScript type checking

## 🔧 Configuration

### API Configuration

Update the API base URL in `.env`:
```env
VITE_API_URL=http://your-api-url/api
```

### SignalR Configuration

Configure the SignalR hub URL:
```env
VITE_SIGNALR_HUB_URL=http://your-api-url/chatHub
```

### Google OAuth

Set up Google OAuth credentials:
```env
VITE_GOOGLE_CLIENT_ID=your-google-client-id
```

## 🎨 Customization

### Theme Colors

Edit `tailwind.config.js` to customize the color scheme:

```javascript
theme: {
  extend: {
    colors: {
      primary: {
        // Your primary color palette
      },
      secondary: {
        // Your secondary color palette
      }
    }
  }
}
```

### Components

All components are fully customizable and located in `src/components/`.

## 📱 Features Overview

### Authentication
- Email/Password login
- Google OAuth integration
- JWT token management
- Auto-refresh tokens
- Protected routes

### Chat Interface
- Real-time messaging via SignalR
- Message history
- Typing indicators
- Markdown support
- Code highlighting
- File attachments (coming soon)

### Conversation Management
- Create new conversations
- Load conversation history
- Delete conversations
- Search conversations
- Pin important chats

### User Features
- User profile management
- Preferences settings
- Daily prompts
- Session management
- Business context

### UI/UX
- Responsive sidebar
- Mobile-optimized
- Dark mode (coming soon)
- Command palette
- Toast notifications
- Loading states
- Error boundaries

## 🚀 Deployment

### Production Build

```bash
npm run build
```

This creates an optimized production build in the `dist/` directory.

### Docker

```dockerfile
FROM node:18-alpine as builder
WORKDIR /app
COPY package*.json ./
RUN npm ci
COPY . .
RUN npm run build

FROM nginx:alpine
COPY --from=builder /app/dist /usr/share/nginx/html
COPY nginx.conf /etc/nginx/conf.d/default.conf
EXPOSE 80
CMD ["nginx", "-g", "daemon off;"]
```

### Environment Variables for Production

```env
VITE_API_URL=https://api.tinkergenie.com/api
VITE_SIGNALR_HUB_URL=https://api.tinkergenie.com/chatHub
VITE_GOOGLE_CLIENT_ID=production-google-client-id
```

## 🔐 Security Considerations

- All API calls use JWT authentication
- Tokens stored in memory and localStorage with encryption
- XSS protection via React's built-in escaping
- CSRF protection via SameSite cookies
- Content Security Policy headers
- HTTPS required in production

## 🧪 Testing

### Unit Tests
```bash
npm run test
```

### E2E Tests
```bash
npm run test:e2e
```

## 📝 API Endpoints Used

### Authentication
- `POST /api/auth/login` - User login
- `POST /api/auth/google` - Google OAuth
- `POST /api/auth/logout` - Logout
- `GET /api/auth/validate` - Validate token
- `POST /api/auth/refresh` - Refresh token

### Chat
- `POST /api/chat/message` - Send message
- `POST /api/chat/session/start` - Start session
- `POST /api/chat/new` - New chat
- `GET /api/chat/daily-prompt` - Get daily prompt

### Conversations
- `GET /api/conversations` - List conversations
- `GET /api/conversations/:id` - Get conversation
- `DELETE /api/conversations/:id` - Delete conversation
- `PUT /api/conversations/:id` - Update conversation

### User
- `GET /api/user/profile` - Get profile
- `PUT /api/user/profile` - Update profile
- `GET /api/user/preferences` - Get preferences
- `PUT /api/user/preferences` - Update preferences

## 🤝 Contributing

1. Fork the repository
2. Create your feature branch (`git checkout -b feature/AmazingFeature`)
3. Commit your changes (`git commit -m 'Add some AmazingFeature'`)
4. Push to the branch (`git push origin feature/AmazingFeature`)
5. Open a Pull Request

## 📄 License

This project is proprietary and confidential.

## 🆘 Support

For issues or questions, please contact the development team.

## 🔄 Migration from Existing Frontend

If you're migrating from the existing frontend examples:

1. **Copy any custom styles** from the old CSS files
2. **Update API endpoints** to match your backend
3. **Import existing components** and convert to TypeScript
4. **Test authentication flow** thoroughly
5. **Verify SignalR connection** for real-time features

## 🎯 Next Steps

1. Install dependencies: `npm install`
2. Configure environment variables
3. Start your .NET API backend
4. Run `npm run dev`
5. Navigate to `http://localhost:3000`
6. Login or register a new account
7. Start using TinkerGenie!

## 📊 Performance Optimizations

- Code splitting with React.lazy
- Image optimization
- Bundle size optimization
- Lazy loading of components
- Memoization of expensive computations
- Virtual scrolling for long lists
- Service Worker for offline support

## 🔮 Roadmap

- [ ] Dark mode support
- [ ] File attachments
- [ ] Voice messages
- [ ] Video calls
- [ ] Team collaboration
- [ ] Analytics dashboard
- [ ] Mobile app (React Native)
- [ ] Desktop app (Electron)

---

Built with ❤️ for gym owners and business leaders.
