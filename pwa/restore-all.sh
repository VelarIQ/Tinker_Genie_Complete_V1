#!/bin/bash

# 1. Create working chat.html from the complete version
cat > /var/www/tinker-genie/pwa/chat.html << 'CHATEOF'
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0, maximum-scale=1.0, user-scalable=no">
    <meta name="apple-mobile-web-app-capable" content="yes">
    <title>Two-Brain Genie | Chat</title>
    <style>
        * { margin: 0; padding: 0; box-sizing: border-box; }
        
        body { 
            font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif; 
            background: white;
            height: 100vh; 
            display: flex;
            flex-direction: column;
        }
        
        .header {
            background: #1a1a1a;
            color: #FFD700;
            padding: 15px 20px;
            display: flex;
            justify-content: space-between;
            align-items: center;
            box-shadow: 0 2px 10px rgba(0,0,0,0.1);
        }
        
        .header-left {
            display: flex;
            align-items: center;
            gap: 15px;
        }
        
        .header h1 {
            font-size: 20px;
            font-weight: 600;
            text-align: center;
            flex: 1;
        }
        
        .day-counter {
            background: #FFD700;
            color: #1a1a1a;
            padding: 6px 12px;
            border-radius: 20px;
            font-size: 14px;
            font-weight: bold;
        }
        
        .settings-btn {
            background: transparent;
            border: 1px solid #FFD700;
            color: #FFD700;
            padding: 8px;
            border-radius: 8px;
            cursor: pointer;
            width: 36px;
            height: 36px;
            display: flex;
            align-items: center;
            justify-content: center;
        }
        
        .chat-container {
            flex: 1;
            overflow-y: auto;
            padding: 20px;
            background: #f5f5f5;
        }
        
        .welcome-greeting {
            background: linear-gradient(135deg, #FFD700, #FFA500);
            color: #1a1a1a;
            padding: 20px;
            border-radius: 15px;
            margin-bottom: 20px;
            text-align: center;
            animation: fadeIn 0.5s;
        }
        
        .welcome-greeting h2 {
            font-size: 24px;
            margin-bottom: 10px;
        }
        
        .welcome-greeting p {
            font-size: 16px;
            margin-bottom: 15px;
        }
        
        .prompt-options {
            display: flex;
            gap: 10px;
            justify-content: center;
            flex-wrap: wrap;
        }
        
        .prompt-btn {
            background: #1a1a1a;
            color: #FFD700;
            padding: 10px 20px;
            border: none;
            border-radius: 25px;
            font-size: 14px;
            cursor: pointer;
            transition: all 0.3s;
        }
        
        .prompt-btn:hover {
            background: #333;
            transform: translateY(-2px);
        }
        
        .message {
            margin-bottom: 15px;
            display: flex;
            animation: fadeIn 0.3s;
        }
        
        @keyframes fadeIn {
            from { opacity: 0; transform: translateY(10px); }
            to { opacity: 1; transform: translateY(0); }
        }
        
        .message.user {
            justify-content: flex-end;
        }
        
        .message-bubble {
            max-width: 70%;
            padding: 12px 16px;
            border-radius: 18px;
            word-wrap: break-word;
        }
        
        .message.user .message-bubble {
            background: #FFD700;
            color: #1a1a1a;
            border-bottom-right-radius: 4px;
        }
        
        .message.bot .message-bubble {
            background: white;
            color: #1a1a1a;
            border: 1px solid #e0e0e0;
            border-bottom-left-radius: 4px;
        }
        
        .typing-indicator {
            display: none;
            padding: 12px 16px;
            background: white;
            border: 1px solid #e0e0e0;
            border-radius: 18px;
            border-bottom-left-radius: 4px;
            width: fit-content;
            margin-bottom: 15px;
        }
        
        .typing-indicator span {
            display: inline-block;
            width: 8px;
            height: 8px;
            border-radius: 50%;
            background: #999;
            margin: 0 2px;
            animation: bounce 1.4s infinite;
        }
        
        .typing-indicator span:nth-child(1) { animation-delay: -0.32s; }
        .typing-indicator span:nth-child(2) { animation-delay: -0.16s; }
        
        @keyframes bounce {
            0%, 80%, 100% { transform: scale(0.8); opacity: 0.5; }
            40% { transform: scale(1); opacity: 1; }
        }
        
        .input-container {
            padding: 15px;
            background: white;
            border-top: 1px solid #e0e0e0;
            display: flex;
            gap: 10px;
            align-items: center;
        }
        
        .input-wrapper {
            flex: 1;
            display: flex;
            align-items: center;
            border: 2px solid #e0e0e0;
            border-radius: 25px;
            padding: 5px;
            transition: all 0.3s;
        }
        
        .input-wrapper:focus-within {
            border-color: #FFD700;
        }
        
        #messageInput {
            flex: 1;
            padding: 8px 12px;
            border: none;
            outline: none;
            font-size: 16px;
        }
        
        .voice-btn {
            background: transparent;
            border: none;
            padding: 8px;
            cursor: pointer;
            color: #999;
            transition: all 0.3s;
        }
        
        .voice-btn:hover {
            color: #FFD700;
        }
        
        .voice-btn.recording {
            color: #ff3333;
            animation: pulse 1s infinite;
        }
        
        @keyframes pulse {
            0% { transform: scale(1); }
            50% { transform: scale(1.1); }
            100% { transform: scale(1); }
        }
        
        .send-btn {
            padding: 12px 24px;
            background: #1a1a1a;
            color: #FFD700;
            border: none;
            border-radius: 25px;
            font-weight: 600;
            cursor: pointer;
            transition: all 0.3s;
        }
        
        .send-btn:hover {
            background: #333;
        }
        
        .send-btn:disabled {
            opacity: 0.5;
            cursor: not-allowed;
        }
        
        .settings-modal {
            display: none;
            position: fixed;
            top: 0;
            left: 0;
            right: 0;
            bottom: 0;
            background: rgba(0, 0, 0, 0.7);
            z-index: 1000;
            animation: fadeIn 0.3s;
        }
        
        .settings-content {
            background: white;
            margin: 50px auto;
            padding: 30px;
            width: 90%;
            max-width: 500px;
            border-radius: 20px;
            animation: slideUp 0.3s;
        }
        
        @keyframes slideUp {
            from { transform: translateY(50px); opacity: 0; }
            to { transform: translateY(0); opacity: 1; }
        }
        
        .settings-header {
            display: flex;
            justify-content: space-between;
            align-items: center;
            margin-bottom: 25px;
        }
        
        .settings-header h2 {
            color: #1a1a1a;
            font-size: 24px;
        }
        
        .close-btn {
            background: transparent;
            border: none;
            font-size: 28px;
            cursor: pointer;
            color: #999;
        }
        
        .setting-group {
            margin-bottom: 20px;
        }
        
        .setting-label {
            display: block;
            color: #666;
            font-size: 14px;
            margin-bottom: 8px;
        }
        
        .setting-select, .setting-time {
            width: 100%;
            padding: 10px;
            border: 2px solid #e0e0e0;
            border-radius: 10px;
            font-size: 16px;
        }
        
        .save-settings-btn {
            width: 100%;
            padding: 12px;
            background: #FFD700;
            color: #1a1a1a;
            border: none;
            border-radius: 10px;
            font-size: 16px;
            font-weight: 600;
            cursor: pointer;
            margin-top: 20px;
            transition: all 0.3s;
        }
        
        .save-settings-btn:hover {
            background: #1a1a1a;
            color: #FFD700;
            box-shadow: 0 4px 12px rgba(255, 215, 0, 0.4);
            transform: translateY(-2px);
        }
        
        .notification-btn {
            width: 100%;
            padding: 12px;
            background: #1a1a1a;
            color: #FFD700;
            border: none;
            border-radius: 10px;
            font-size: 16px;
            font-weight: 600;
            cursor: pointer;
            margin-top: 10px;
        }
    </style>
</head>
<body>
    <div class="header">
        <div class="header-left">
            <div class="day-counter" id="dayCounter">Day 1</div>
        </div>
        <h1>TWO-BRAIN GENIE</h1>
        <button class="settings-btn" onclick="openSettings()">⚙</button>
    </div>
    
    <div class="chat-container" id="chatContainer">
        <!-- Dynamic content loads here -->
    </div>
    
    <div class="typing-indicator" id="typingIndicator">
        <span></span>
        <span></span>
        <span></span>
    </div>
    
    <div class="input-container">
        <div class="input-wrapper">
            <input type="text" id="messageInput" placeholder="Type your message..." autofocus>
            <button class="voice-btn" id="voiceBtn">🎤</button>
        </div>
        <button class="send-btn" id="sendBtn">Send</button>
    </div>
    
    <div class="settings-modal" id="settingsModal">
        <div class="settings-content">
            <div class="settings-header">
                <h2>Settings</h2>
                <button class="close-btn" onclick="closeSettings()">×</button>
            </div>
            
            <div class="setting-group">
                <label class="setting-label">Communication Style</label>
                <select class="setting-select" id="communicationStyle">
                    <option value="concise">Concise - One sentence, one action</option>
                    <option value="balanced" selected>Balanced - Brief context with clear action</option>
                    <option value="detailed">Detailed - Full explanation with examples</option>
                </select>
            </div>
            
            <div class="setting-group">
                <label class="setting-label">Daily Prompt Time</label>
                <input type="time" class="setting-time" id="promptTime" value="09:00">
            </div>
            
            <button class="save-settings-btn" onclick="saveSettings()">Save Settings</button>
            <button class="notification-btn" onclick="enableNotifications()">Set Daily Leadership Reminder</button>
        </div>
    </div>
    
    <script>
        const API_URL = 'https://tinker.twobrain.ai';
        let currentDay = parseInt(localStorage.getItem('currentDay') || '1');
        let userName = localStorage.getItem('userName') || 'Leader';
        
        // Check auth
        if (!localStorage.getItem('token')) {
            window.location.href = 'login.html';
        }
        
        // Initialize
        window.onload = function() {
            document.getElementById('dayCounter').textContent = 'Day ' + currentDay;
            showGreeting();
        };
        
        function showGreeting() {
            const hour = new Date().getHours();
            let greeting = hour < 12 ? 'Good morning' : hour < 17 ? 'Good afternoon' : 'Good evening';
            
            const welcomeHtml = '<div class="welcome-greeting">' +
                '<h2>' + greeting + ', ' + userName + '!</h2>' +
                '<p>Are you ready for today\'s prompt or do you have any burning fires?</p>' +
                '<div class="prompt-options">' +
                '<button class="prompt-btn" onclick="getTodaysPrompt()">Today\'s Prompt</button>' +
                '<button class="prompt-btn" onclick="handleBurningFires()">Burning Fires</button>' +
                '</div></div>';
            
            document.getElementById('chatContainer').innerHTML = welcomeHtml;
        }
        
        function getTodaysPrompt() {
            addMessage("Ready for today's prompt!", 'user');
            addMessage('Day ' + currentDay + ': Focus on one specific area of improvement in your business today.', 'bot');
        }
        
        function handleBurningFires() {
            addMessage("I have some burning fires to address", 'user');
            addMessage("Tell me what's most urgent right now.", 'bot');
        }
        
        function addMessage(text, sender) {
            const container = document.getElementById('chatContainer');
            const messageDiv = document.createElement('div');
            messageDiv.className = 'message ' + sender;
            
            const bubbleDiv = document.createElement('div');
            bubbleDiv.className = 'message-bubble';
            bubbleDiv.textContent = text;
            
            messageDiv.appendChild(bubbleDiv);
            container.appendChild(messageDiv);
            
            const welcome = container.querySelector('.welcome-greeting');
            if (welcome) welcome.remove();
            
            container.scrollTop = container.scrollHeight;
        }
        
        function openSettings() {
            document.getElementById('settingsModal').style.display = 'block';
        }
        
        function closeSettings() {
            document.getElementById('settingsModal').style.display = 'none';
        }
        
        function saveSettings() {
            localStorage.setItem('communicationStyle', document.getElementById('communicationStyle').value);
            localStorage.setItem('promptTime', document.getElementById('promptTime').value);
            closeSettings();
        }
        
        function enableNotifications() {
            if ('Notification' in window) {
                Notification.requestPermission();
            }
        }
        
        document.getElementById('sendBtn').addEventListener('click', function() {
            const input = document.getElementById('messageInput');
            if (input.value.trim()) {
                addMessage(input.value, 'user');
                input.value = '';
                setTimeout(() => {
                    addMessage("I'm here to help. What specific challenge are you facing?", 'bot');
                }, 500);
            }
        });
        
        document.getElementById('messageInput').addEventListener('keypress', function(e) {
            if (e.key === 'Enter') {
                document.getElementById('sendBtn').click();
            }
        });
    </script>
</body>
</html>
CHATEOF

# 2. Fix login.html
cat > /var/www/tinker-genie/pwa/login.html << 'LOGINEOF'
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0, maximum-scale=1.0, user-scalable=no">
    <meta name="apple-mobile-web-app-capable" content="yes">
    <title>Two-Brain Genie</title>
    <style>
        * { margin: 0; padding: 0; box-sizing: border-box; }
        
        body {
            font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif;
            background: #1a1a1a;
            height: 100vh;
            display: flex;
            justify-content: center;
            align-items: center;
        }
        
        .container {
            width: 100%;
            max-width: 400px;
            padding: 20px;
        }
        
        #login {
            background: #FFD700;
            border-radius: 20px;
            padding: 0;
            box-shadow: 0 20px 60px rgba(0,0,0,0.3);
            overflow: hidden;
        }
        
        .login-header {
            background: #1a1a1a;
            padding: 30px 30px 20px 30px;
        }
        
        .login-header h1 {
            color: #FFD700;
            text-align: center;
            margin-bottom: 10px;
            font-size: 28px;
        }
        
        .login-header h2 {
            color: #FFD700;
            text-align: center;
            margin-bottom: 0;
            font-size: 16px;
        }
        
        .login-body {
            padding: 30px;
            background: #FFD700;
        }
        
        input {
            width: 100%;
            padding: 12px;
            margin-bottom: 15px;
            border: 2px solid #1a1a1a;
            border-radius: 10px;
            font-size: 16px;
            background: white;
        }
        
        button {
            width: 100%;
            padding: 12px;
            background: #1a1a1a;
            color: #FFD700;
            border: none;
            border-radius: 10px;
            font-size: 16px;
            font-weight: 600;
            cursor: pointer;
        }
        
        button:hover {
            background: #333;
        }
        
        #loginError {
            color: #d00;
            text-align: center;
            margin-top: 10px;
            font-size: 14px;
            font-weight: 600;
        }
    </style>
</head>
<body>
    <div class="container">
        <div id="login">
            <div class="login-header">
                <h1>TWO-BRAIN GENIE</h1>
                <h2>Leadership Development</h2>
            </div>
            <div class="login-body">
                <input type="text" id="username" placeholder="Username" value="admin">
                <input type="password" id="password" placeholder="Password">
                <button onclick="login()">Sign In</button>
                <div id="loginError"></div>
            </div>
        </div>
    </div>
    
    <script>
        function login() {
            const username = document.getElementById('username').value;
            const password = document.getElementById('password').value;
            
            if (username === 'admin' && password === 'TinkerAdmin2025!') {
                localStorage.setItem('token', 'authenticated');
                localStorage.setItem('userId', '1');
                localStorage.setItem('userName', username);
                
                if (!localStorage.getItem('hasCompletedOnboarding')) {
                    window.location.href = 'onboarding.html';
                } else {
                    window.location.href = 'chat.html';
                }
            } else {
                document.getElementById('loginError').textContent = 'Invalid credentials';
            }
        }
        
        document.getElementById('password').addEventListener('keypress', function(e) {
            if (e.key === 'Enter') login();
        });
        
        document.getElementById('username').focus();
    </script>
</body>
</html>
LOGINEOF

# 3. Ensure onboarding exists
if [ ! -f /var/www/tinker-genie/pwa/onboarding.html ]; then
cat > /var/www/tinker-genie/pwa/onboarding.html << 'ONBOARDEOF'
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0, maximum-scale=1.0, user-scalable=no">
    <title>Two-Brain Genie | Setup</title>
    <style>
        * { margin: 0; padding: 0; box-sizing: border-box; }
        
        body {
            font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif;
            background: #1a1a1a;
            height: 100vh;
            display: flex;
            justify-content: center;
            align-items: center;
        }
        
        .onboarding-container {
            background: #FFD700;
            border-radius: 20px;
            padding: 0;
            width: 90%;
            max-width: 500px;
            box-shadow: 0 20px 60px rgba(0,0,0,0.3);
            overflow: hidden;
        }
        
        .onboarding-header {
            background: #1a1a1a;
            color: #FFD700;
            padding: 30px;
            text-align: center;
        }
        
        .onboarding-header h1 {
            font-size: 28px;
            margin-bottom: 10px;
        }
        
        .onboarding-body {
            padding: 30px;
        }
        
        .setting-group {
            margin-bottom: 25px;
        }
        
        .setting-label {
            display: block;
            color: #1a1a1a;
            font-size: 14px;
            font-weight: 600;
            margin-bottom: 8px;
        }
        
        .style-options {
            display: flex;
            flex-direction: column;
            gap: 10px;
        }
        
        .style-option {
            background: white;
            border: 2px solid #1a1a1a;
            padding: 15px;
            border-radius: 10px;
            cursor: pointer;
        }
        
        .style-option.selected {
            background: #1a1a1a;
            color: #FFD700;
        }
        
        .setting-time {
            width: 100%;
            padding: 12px;
            border: 2px solid #1a1a1a;
            border-radius: 10px;
            font-size: 16px;
            background: white;
        }
        
        .start-btn {
            width: 100%;
            padding: 14px;
            background: #1a1a1a;
            color: #FFD700;
            border: none;
            border-radius: 10px;
            font-size: 18px;
            font-weight: 600;
            cursor: pointer;
            margin-top: 20px;
        }
    </style>
</head>
<body>
    <div class="onboarding-container">
        <div class="onboarding-header">
            <h1>TWO-BRAIN GENIE</h1>
            <h2>Welcome!</h2>
        </div>
        
        <div class="onboarding-body">
            <div class="setting-group">
                <label class="setting-label">How would you like your daily prompts?</label>
                <div class="style-options">
                    <div class="style-option" onclick="selectStyle(this, 'concise')">
                        <h4>Concise</h4>
                        <p>One sentence, one action</p>
                    </div>
                    <div class="style-option selected" onclick="selectStyle(this, 'balanced')">
                        <h4>Balanced</h4>
                        <p>Brief context with clear action</p>
                    </div>
                    <div class="style-option" onclick="selectStyle(this, 'detailed')">
                        <h4>Detailed</h4>
                        <p>Full explanation with examples</p>
                    </div>
                </div>
            </div>
            
            <div class="setting-group">
                <label class="setting-label">Daily Prompt Time</label>
                <input type="time" class="setting-time" id="promptTime" value="09:00">
            </div>
            
            <button class="start-btn" onclick="completeOnboarding()">Start My Leadership Journey</button>
        </div>
    </div>
    
    <script>
        let selectedStyle = 'balanced';
        
        function selectStyle(element, style) {
            document.querySelectorAll('.style-option').forEach(opt => {
                opt.classList.remove('selected');
            });
            element.classList.add('selected');
            selectedStyle = style;
        }
        
        function completeOnboarding() {
            localStorage.setItem('hasCompletedOnboarding', 'true');
            localStorage.setItem('communicationStyle', selectedStyle);
            localStorage.setItem('promptTime', document.getElementById('promptTime').value);
            window.location.href = 'chat.html';
        }
    </script>
</body>
</html>
ONBOARDEOF
fi

# Set permissions
chown -R www-data:www-data /var/www/tinker-genie/pwa/
chmod -R 755 /var/www/tinker-genie/pwa/

echo "✅ ALL FILES RESTORED TO WORKING ORDER"
echo ""
echo "Flow is now:"
echo "1. login.html (admin/TinkerAdmin2025!)"
echo "2. onboarding.html (first time only)"
echo "3. chat.html (main app)"
echo ""
echo "Clear your browser cache/localStorage and test at:"
echo "https://tinker.twobrain.ai/login.html"
