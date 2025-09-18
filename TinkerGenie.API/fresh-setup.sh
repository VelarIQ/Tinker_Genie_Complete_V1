#!/bin/bash
# ============================================
# TINKER GENIE - CLEAN SLATE SETUP
# Interactive setup with questions
# ============================================

set -e

echo "🧹 TINKER GENIE - COMPLETE CLEANUP & FRESH SETUP"
echo "================================================"
echo ""

# Function to ask questions
ask_question() {
    local question=$1
    local default=$2
    local var_name=$3
    
    echo -n "$question"
    if [ ! -z "$default" ]; then
        echo -n " [$default]"
    fi
    echo -n ": "
    
    read answer
    if [ -z "$answer" ]; then
        answer=$default
    fi
    
    eval "$var_name='$answer'"
}

# Function to confirm
confirm() {
    local question=$1
    echo -n "$question (y/n): "
    read answer
    if [ "$answer" != "y" ]; then
        return 1
    fi
    return 0
}

# 1. GATHER INFORMATION
echo "📋 SETUP QUESTIONS"
echo "=================="
echo ""

ask_question "What is your Google Drive folder ID for curriculum?" "" GOOGLE_FOLDER_ID
ask_question "Do you have a Google service account JSON file ready? (y/n)" "n" HAS_SERVICE_ACCOUNT
ask_question "What should be the admin password for the system?" "TinkerGenie2025Admin" ADMIN_PASSWORD
ask_question "Do you want to set up n8n automation now? (y/n)" "y" SETUP_N8N
ask_question "Do you want the PWA (mobile app)? (y/n)" "y" SETUP_PWA
ask_question "Do you want the desktop widget? (y/n)" "y" SETUP_WIDGET
ask_question "What is the main domain? (tinker.twobrain.ai already has SSL)" "tinker.twobrain.ai" DOMAIN

echo ""
echo "📝 CONFIGURATION SUMMARY:"
echo "========================"
echo "Domain: $DOMAIN"
echo "Google Drive Folder: $GOOGLE_FOLDER_ID"
echo "Setup PWA: $SETUP_PWA"
echo "Setup Widget: $SETUP_WIDGET"
echo "Setup n8n: $SETUP_N8N"
echo ""

if ! confirm "Proceed with setup?"; then
    echo "Setup cancelled"
    exit 0
fi

# 2. CLEANUP EXISTING
echo ""
echo "🧹 Cleaning up existing setup..."

# Stop services
pm2 stop all 2>/dev/null || true
pm2 delete all 2>/dev/null || true

# Clean directories
rm -rf /var/www/tinker-genie/pwa 2>/dev/null || true
rm -rf /var/www/tinker-genie/widget 2>/dev/null || true

# 3. CREATE BASE STRUCTURE
echo "📁 Creating base structure..."

cd /var/www/tinker-genie/TinkerGenie.API

# Create directories
mkdir -p /var/www/tinker-genie/{pwa,widget,data,logs}

# 4. UPDATE CONFIGURATION
echo "⚙️ Updating configuration..."

cat > appsettings.json << EOF
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "ConnectionStrings": {
    "DefaultConnection": "Host=161.35.5.159;Database=tinker_genie;Username=genie_admin;Password=***PASSWORD***",
    "Redis": "localhost:6379,password=***PASSWORD***",
    "TBBDatabase": "Server=tcp:2brain.database.windows.net,1433;Initial Catalog=TwoBrain_Dev;Persist Security Info=False;User ID=TwoBrainAI;Password=genie-twobrain-ai-2025;MultipleActiveResultSets=False;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;"
  },
  "OpenAI": {
    "ApiKey": "**API_KEY**",
    "Model": "gpt-4o-mini"
  },
  "Weaviate": {
    "Url": "https://8xuvunaprigegm92uv5xwa.c0.us-west3.gcp.weaviate.cloud",
    "ApiKey": "cHc2YzhwMFV3OHpZQ1hrVV8xU2Q4cmhKTEk3ZE5ZQ1JoY0ZwTGdNMlZkRUNqQm1NYVhKLzZ0NllYK0JzPV92MjAw"
  },
  "GoogleDrive": {
    "FolderId": "$GOOGLE_FOLDER_ID",
    "ServiceAccountPath": "/var/www/tinker-genie/data/service-account.json"
  },
  "Admin": {
    "Password": "$ADMIN_PASSWORD"
  }
}
EOF

# 5. SETUP PWA IF REQUESTED
if [ "$SETUP_PWA" = "y" ]; then
    echo "📱 Setting up PWA..."
    
    cat > /var/www/tinker-genie/pwa/index.html << 'EOF'
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0, maximum-scale=1.0, user-scalable=no">
    <meta name="apple-mobile-web-app-capable" content="yes">
    <meta name="apple-mobile-web-app-status-bar-style" content="default">
    <meta name="apple-mobile-web-app-title" content="TB Genie">
    <title>Two-Brain Genie</title>
    <link rel="manifest" href="/manifest.json">
    <link rel="apple-touch-icon" href="/icon-180.png">
    <style>
        * { 
            margin: 0; 
            padding: 0; 
            box-sizing: border-box; 
            -webkit-tap-highlight-color: transparent;
        }
        
        body {
            font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif;
            background: linear-gradient(135deg, #FF6B35 0%, #1A1A2E 100%);
            height: 100vh;
            height: -webkit-fill-available;
            color: white;
            overflow: hidden;
        }
        
        .container {
            height: 100vh;
            height: -webkit-fill-available;
            display: flex;
            flex-direction: column;
            max-width: 600px;
            margin: 0 auto;
        }
        
        .header {
            background: rgba(255,255,255,0.1);
            backdrop-filter: blur(10px);
            -webkit-backdrop-filter: blur(10px);
            padding: 15px;
            text-align: center;
            flex-shrink: 0;
        }
        
        .header h1 {
            font-size: 24px;
            font-weight: 600;
            display: flex;
            align-items: center;
            justify-content: center;
            gap: 10px;
        }
        
        .chat-container {
            flex: 1;
            overflow-y: auto;
            padding: 20px;
            -webkit-overflow-scrolling: touch;
        }
        
        .message {
            margin: 10px 0;
            padding: 12px 16px;
            border-radius: 18px;
            max-width: 80%;
            word-wrap: break-word;
            animation: fadeIn 0.3s ease;
        }
        
        @keyframes fadeIn {
            from { opacity: 0; transform: translateY(10px); }
            to { opacity: 1; transform: translateY(0); }
        }
        
        .message.user {
            background: #FF6B35;
            margin-left: auto;
            color: white;
        }
        
        .message.genie {
            background: rgba(255,255,255,0.2);
            color: white;
        }
        
        .typing-indicator {
            display: none;
            padding: 12px 16px;
            background: rgba(255,255,255,0.1);
            border-radius: 18px;
            width: fit-content;
            margin: 10px 0;
        }
        
        .typing-indicator.show {
            display: block;
        }
        
        .typing-indicator span {
            display: inline-block;
            width: 8px;
            height: 8px;
            border-radius: 50%;
            background: white;
            margin: 0 2px;
            animation: bounce 1.4s infinite ease-in-out;
        }
        
        .typing-indicator span:nth-child(1) { animation-delay: -0.32s; }
        .typing-indicator span:nth-child(2) { animation-delay: -0.16s; }
        
        @keyframes bounce {
            0%, 80%, 100% { transform: scale(0.8); opacity: 0.5; }
            40% { transform: scale(1); opacity: 1; }
        }
        
        .input-area {
            padding: 15px;
            background: rgba(0,0,0,0.3);
            display: flex;
            gap: 10px;
            flex-shrink: 0;
            padding-bottom: calc(15px + env(safe-area-inset-bottom));
        }
        
        #messageInput {
            flex: 1;
            padding: 12px 16px;
            border: none;
            border-radius: 25px;
            font-size: 16px;
            background: white;
            color: #333;
        }
        
        #messageInput:focus {
            outline: none;
        }
        
        #sendBtn {
            padding: 12px 24px;
            background: #FF6B35;
            color: white;
            border: none;
            border-radius: 25px;
            font-weight: 600;
            cursor: pointer;
            touch-action: manipulation;
        }
        
        #sendBtn:active {
            transform: scale(0.95);
        }
        
        .welcome-message {
            text-align: center;
            padding: 40px 20px;
            opacity: 0.8;
        }
        
        .welcome-message h2 {
            margin-bottom: 10px;
        }
    </style>
</head>
<body>
    <div class="container">
        <div class="header">
            <h1>🧠 Two-Brain Genie</h1>
        </div>
        
        <div class="chat-container" id="chatContainer">
            <div class="welcome-message">
                <h2>Welcome to Your Leadership Coach!</h2>
                <p>Ask me anything about leadership, business, or personal growth.</p>
            </div>
        </div>
        
        <div class="typing-indicator" id="typingIndicator">
            <span></span>
            <span></span>
            <span></span>
        </div>
        
        <div class="input-area">
            <input 
                type="text" 
                id="messageInput" 
                placeholder="Ask your Genie..." 
                autocomplete="off"
                autocorrect="on"
                autocapitalize="sentences"
                spellcheck="true"
            >
            <button id="sendBtn">Send</button>
        </div>
    </div>
    
    <script>
        const API_URL = window.location.protocol + '//' + window.location.host + '/api/chat';
        const chatContainer = document.getElementById('chatContainer');
        const messageInput = document.getElementById('messageInput');
        const sendBtn = document.getElementById('sendBtn');
        const typingIndicator = document.getElementById('typingIndicator');
        
        // Load conversation history from localStorage
        const conversationHistory = JSON.parse(localStorage.getItem('tb-conversation') || '[]');
        
        // Display history on load
        if (conversationHistory.length > 0) {
            document.querySelector('.welcome-message').style.display = 'none';
            conversationHistory.forEach(msg => {
                addMessage(msg.content, msg.isUser, false);
            });
        }
        
        function addMessage(content, isUser, save = true) {
            const msg = document.createElement('div');
            msg.className = 'message ' + (isUser ? 'user' : 'genie');
            msg.textContent = content;
            chatContainer.appendChild(msg);
            chatContainer.scrollTop = chatContainer.scrollHeight;
            
            if (save) {
                conversationHistory.push({ content, isUser });
                // Keep only last 50 messages
                if (conversationHistory.length > 50) {
                    conversationHistory.shift();
                }
                localStorage.setItem('tb-conversation', JSON.stringify(conversationHistory));
            }
        }
        
        function showTyping() {
            typingIndicator.classList.add('show');
            chatContainer.appendChild(typingIndicator);
            chatContainer.scrollTop = chatContainer.scrollHeight;
        }
        
        function hideTyping() {
            typingIndicator.classList.remove('show');
        }
        
        async function sendMessage() {
            const message = messageInput.value.trim();
            if (!message) return;
            
            // Clear welcome message
            const welcome = document.querySelector('.welcome-message');
            if (welcome) welcome.style.display = 'none';
            
            addMessage(message, true);
            messageInput.value = '';
            
            showTyping();
            
            try {
                const response = await fetch(API_URL, {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ 
                        message,
                        userId: localStorage.getItem('tb-user-id') || 'pwa-user'
                    })
                });
                
                const data = await response.json();
                hideTyping();
                addMessage(data.message || data.reply || 'I understand. Could you tell me more?', false);
            } catch (error) {
                hideTyping();
                addMessage('I\'m having trouble connecting. Please check your connection and try again.', false);
            }
        }
        
        sendBtn.onclick = sendMessage;
        messageInput.onkeypress = (e) => {
            if (e.key === 'Enter' && !e.shiftKey) {
                e.preventDefault();
                sendMessage();
            }
        };
        
        // PWA install prompt
        let deferredPrompt;
        window.addEventListener('beforeinstallprompt', (e) => {
            e.preventDefault();
            deferredPrompt = e;
        });
        
        // Service Worker
        if ('serviceWorker' in navigator) {
            navigator.serviceWorker.register('/sw.js').then(reg => {
                console.log('Service Worker registered');
            });
        }
        
        // Set user ID if not exists
        if (!localStorage.getItem('tb-user-id')) {
            localStorage.setItem('tb-user-id', 'user-' + Date.now());
        }
    </script>
</body>
</html>
EOF

    # Create manifest
    cat > /var/www/tinker-genie/pwa/manifest.json << EOF
{
  "name": "Two-Brain Genie",
  "short_name": "TB Genie",
  "description": "Your AI Leadership Coach",
  "start_url": "/",
  "display": "standalone",
  "background_color": "#1A1A2E",
  "theme_color": "#FF6B35",
  "orientation": "portrait",
  "icons": [
    {
      "src": "/icon-192.png",
      "sizes": "192x192",
      "type": "image/png",
      "purpose": "any maskable"
    },
    {
      "src": "/icon-512.png",
      "sizes": "512x512",
      "type": "image/png"
    }
  ]
}
EOF

    # Create service worker
    cat > /var/www/tinker-genie/pwa/sw.js << 'EOF'
const CACHE_NAME = 'tb-genie-v1';
const urlsToCache = [
  '/',
  '/manifest.json'
];

self.addEventListener('install', event => {
  event.waitUntil(
    caches.open(CACHE_NAME)
      .then(cache => cache.addAll(urlsToCache))
  );
  self.skipWaiting();
});

self.addEventListener('activate', event => {
  event.waitUntil(clients.claim());
});

self.addEventListener('fetch', event => {
  event.respondWith(
    caches.match(event.request)
      .then(response => response || fetch(event.request))
  );
});
EOF

    # Create placeholder icons
    echo "🎨 Creating icons..."
    # Create simple colored squares as placeholders
    echo '<svg xmlns="http://www.w3.org/2000/svg" width="192" height="192"><rect width="192" height="192" fill="#FF6B35"/><text x="96" y="120" font-size="100" fill="white" text-anchor="middle">🧠</text></svg>' > /var/www/tinker-genie/pwa/icon-192.png
    echo '<svg xmlns="http://www.w3.org/2000/svg" width="512" height="512"><rect width="512" height="512" fill="#FF6B35"/><text x="256" y="300" font-size="200" fill="white" text-anchor="middle">🧠</text></svg>' > /var/www/tinker-genie/pwa/icon-512.png
    echo '<svg xmlns="http://www.w3.org/2000/svg" width="180" height="180"><rect width="180" height="180" fill="#FF6B35"/><text x="90" y="110" font-size="90" fill="white" text-anchor="middle">🧠</text></svg>' > /var/www/tinker-genie/pwa/icon-180.png
fi

# 6. SETUP WIDGET IF REQUESTED
if [ "$SETUP_WIDGET" = "y" ]; then
    echo "🖥️ Setting up widget..."
    
    cat > /var/www/tinker-genie/widget/widget.js << 'EOF'
// Two-Brain Genie Widget
(function() {
    // Don't load on mobile
    if (/iPhone|iPad|iPod|Android/i.test(navigator.userAgent)) {
        return;
    }
    
    const widgetHTML = `
        <div id="tb-genie-widget" style="position: fixed; bottom: 20px; right: 20px; z-index: 99999;">
            <div id="tb-genie-button" style="width: 60px; height: 60px; border-radius: 50%; background: #FF6B35; display: flex; align-items: center; justify-content: center; cursor: pointer; box-shadow: 0 4px 12px rgba(0,0,0,0.15); transition: transform 0.3s;">
                <span style="font-size: 30px;">🧠</span>
            </div>
            <div id="tb-genie-chat" style="position: absolute; bottom: 80px; right: 0; width: 380px; height: 500px; background: white; border-radius: 15px; box-shadow: 0 10px 40px rgba(0,0,0,0.2); display: none; flex-direction: column;">
                <div style="background: linear-gradient(135deg, #FF6B35 0%, #1A1A2E 100%); color: white; padding: 15px; border-radius: 15px 15px 0 0; display: flex; justify-content: space-between; align-items: center;">
                    <span style="font-weight: 600;">Two-Brain Genie</span>
                    <button onclick="tbGenie.close()" style="background: none; border: none; color: white; font-size: 20px; cursor: pointer;">×</button>
                </div>
                <div id="tb-genie-messages" style="flex: 1; overflow-y: auto; padding: 15px;"></div>
                <div style="padding: 15px; border-top: 1px solid #eee; display: flex; gap: 10px;">
                    <input type="text" id="tb-genie-input" placeholder="Ask your question..." style="flex: 1; padding: 10px; border: 1px solid #ddd; border-radius: 20px; outline: none;">
                    <button id="tb-genie-send" style="background: #FF6B35; color: white; border: none; padding: 10px 20px; border-radius: 20px; cursor: pointer;">Send</button>
                </div>
            </div>
        </div>
    `;
    
    const div = document.createElement('div');
    div.innerHTML = widgetHTML;
    document.body.appendChild(div.firstElementChild);
    
    window.tbGenie = {
        isOpen: false,
        apiUrl: window.location.protocol + '//tinker.twobrain.ai/api/chat',
        
        init() {
            document.getElementById('tb-genie-button').onclick = () => this.toggle();
            document.getElementById('tb-genie-send').onclick = () => this.sendMessage();
            document.getElementById('tb-genie-input').onkeypress = (e) => {
                if (e.key === 'Enter') this.sendMessage();
            };
        },
        
        toggle() {
            this.isOpen = !this.isOpen;
            document.getElementById('tb-genie-chat').style.display = this.isOpen ? 'flex' : 'none';
            
            if (this.isOpen && !this.welcomed) {
                this.addMessage('genie', 'Hello! I\'m your Two-Brain Genie. How can I help you today?');
                this.welcomed = true;
            }
        },
        
        close() {
            this.isOpen = false;
            document.getElementById('tb-genie-chat').style.display = 'none';
        },
        
        async sendMessage() {
            const input = document.getElementById('tb-genie-input');
            const message = input.value.trim();
            if (!message) return;
            
            this.addMessage('user', message);
            input.value = '';
            
            try {
                const response = await fetch(this.apiUrl, {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ message })
                });
                
                const data = await response.json();
                this.addMessage('genie', data.message || 'I understand. Let me help you with that.');
            } catch (err) {
                this.addMessage('genie', 'I\'m having connection issues. Please try again in a moment.');
            }
        },
        
        addMessage(sender, text) {
            const container = document.getElementById('tb-genie-messages');
            const message = document.createElement('div');
            message.style.cssText = `margin: 10px 0; padding: 10px 15px; border-radius: 15px; max-width: 70%; ${sender === 'user' ? 'background: #FF6B35; color: white; margin-left: auto;' : 'background: #f0f0f0;'}`;
            message.textContent = text;
            container.appendChild(message);
            container.scrollTop = container.scrollHeight;
        }
    };
    
    tbGenie.init();
})();
EOF
fi

# 7. CONFIGURE NGINX
echo "🔧 Configuring Nginx..."

cat > /etc/nginx/sites-available/tinker << EOF
server {
    listen 80;
    server_name $DOMAIN;
    return 301 https://\$server_name\$request_uri;
}

server {
    listen 443 ssl http2;
    server_name $DOMAIN;
    
    ssl_certificate /etc/letsencrypt/live/$DOMAIN/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/$DOMAIN/privkey.pem;
    ssl_protocols TLSv1.2 TLSv1.3;
    ssl_ciphers HIGH:!aNULL:!MD5;
    
    # Security headers
    add_header X-Frame-Options "SAMEORIGIN" always;
    add_header X-Content-Type-Options "nosniff" always;
    add_header X-XSS-Protection "1; mode=block" always;
    
    # PWA root
    root /var/www/tinker-genie/pwa;
    index index.html;
    
    location / {
        try_files \$uri \$uri/ /index.html;
    }
    
    # Widget
    location /widget.js {
        alias /var/www/tinker-genie/widget/widget.js;
        add_header Access-Control-Allow-Origin "*";
    }
    
    # API proxy
    location /api/ {
        proxy_pass http://localhost:5000/api/;
        proxy_http_version 1.1;
        proxy_set_header Upgrade \$http_upgrade;
        proxy_set_header Connection 'upgrade';
        proxy_set_header Host \$host;
        proxy_cache_bypass \$http_upgrade;
        proxy_set_header X-Real-IP \$remote_addr;
        proxy_set_header X-Forwarded-For \$proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto \$scheme;
    }
}
EOF

ln -sf /etc/nginx/sites-available/tinker /etc/nginx/sites-enabled/
nginx -t && systemctl reload nginx

# 8. START API
echo "🚀 Starting API..."
cd /var/www/tinker-genie/TinkerGenie.API
pm2 start "dotnet run --urls http://0.0.0.0:5000" --name tinker-api
pm2 save
pm2 startup systemd -u root --hp /root

# 9. CREATE SETUP INSTRUCTIONS
cat > /var/www/tinker-genie/SETUP_COMPLETE.md << EOF
# TINKER GENIE SETUP COMPLETE! 🎉

## Access Points:
- **PWA (Mobile)**: https://$DOMAIN
- **API**: https://$DOMAIN/api/
- **Widget**: https://$DOMAIN/widget.js

## Google Drive Setup:
${GOOGLE_FOLDER_ID:+Folder ID: $GOOGLE_FOLDER_ID}

To complete Google Drive integration:
1. Create a service account in Google Cloud Console
2. Download the JSON key
3. Share your Drive folder with the service account email
4. Upload the JSON to: /var/www/tinker-genie/data/service-account.json

## Test Commands:
\`\`\`bash
# Test API
curl https://$DOMAIN/api/chat \\
  -H "Content-Type: application/json" \\
  -d '{"message":"Hello"}'

# Test health
curl https://$DOMAIN/health
\`\`\`

## Widget Embed Code:
\`\`\`html
<script src="https://$DOMAIN/widget.js"></script>
\`\`\`

## Admin Password: $ADMIN_PASSWORD

## Next Steps:
1. Test PWA on mobile devices
2. Set up Google Drive sync
3. Configure n8n automation
4. Import users
EOF

echo ""
echo "✅ SETUP COMPLETE!"
echo ""
cat /var/www/tinker-genie/SETUP_COMPLETE.md
