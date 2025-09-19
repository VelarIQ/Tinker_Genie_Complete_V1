class TinkerGenieApp {
    constructor() {
        this.userData = this.loadUserData();
        this.state = {
            userId: this.getUserId(),
            conversationId: localStorage.getItem('tg_conversationId'),
            currentDay: parseInt(localStorage.getItem('tg_currentDay') || '1'),
            isProcessing: false,
            lastGreeting: this.getLastGreeting()
        };
        this.init();
    }
    
    getUserId() {
        let userId = localStorage.getItem('tg_userId');
        if (!userId) {
            userId = 'user_' + Date.now() + '_' + Math.random().toString(36).substr(2, 9);
            localStorage.setItem('tg_userId', userId);
        }
        return userId;
    }
    
    loadUserData() {
        const stored = localStorage.getItem('tg_userData');
        if (stored) {
            return JSON.parse(stored);
        }
        
        const name = prompt('Welcome! What\'s your first name?') || 'Leader';
        const businessName = prompt('What\'s your gym called?') || 'Your Gym';
        
        const userData = { name, businessName };
        localStorage.setItem('tg_userData', JSON.stringify(userData));
        return userData;
    }
    
    getLastGreeting() {
        const stored = localStorage.getItem('tg_lastGreeting');
        if (!stored) return null;
        
        const { timestamp, period } = JSON.parse(stored);
        const now = new Date();
        const then = new Date(timestamp);
        
        // If it's the same day and same period, don't greet again
        if (now.toDateString() === then.toDateString()) {
            const currentPeriod = this.getTimePeriod();
            if (currentPeriod === period) {
                return { skipGreeting: true };
            }
        }
        return null;
    }
    
    getTimePeriod() {
        const hour = new Date().getHours();
        if (hour < 12) return 'morning';
        if (hour < 17) return 'afternoon';
        return 'evening';
    }
    
    async init() {
        this.updateUserDisplay();
        this.setupEventListeners();
        this.loadMessages();
        
        // Only show greeting if appropriate
        if (!this.state.lastGreeting?.skipGreeting) {
            this.showSmartGreeting();
        }
        
        // Check for notifications after delay
        setTimeout(() => this.checkNotifications(), 8000);
    }
    
    showSmartGreeting() {
        const period = this.getTimePeriod();
        const greeting = `Good ${period}, ${this.userData.name}!`;
        
        // Save greeting state
        localStorage.setItem('tg_lastGreeting', JSON.stringify({
            timestamp: new Date().toISOString(),
            period: period
        }));
        
        this.addMessage(greeting, 'genie', false);
    }
    
    checkNotifications() {
        const notifTime = localStorage.getItem('tg_notificationTime');
        if (!notifTime && Notification.permission === 'default') {
            this.showNotificationPrompt();
        }
    }
    
    showNotificationPrompt() {
        const prompt = document.createElement('div');
        prompt.id = 'notif-prompt';
        prompt.style.cssText = `
            position: fixed;
            bottom: 20px;
            left: 50%;
            transform: translateX(-50%);
            background: #000;
            border: 3px solid #FFD700;
            border-radius: 12px;
            padding: 20px;
            z-index: 10000;
            max-width: 90%;
            width: 350px;
            animation: slideUp 0.5s ease;
        `;
        
        prompt.innerHTML = `
            <h3 style="color: #FFD700; margin: 0 0 10px 0;">Daily Leadership WOD</h3>
            <p style="color: #fff; margin: 0 0 15px 0; font-size: 14px;">
                Get your daily challenge. When should we send it?
            </p>
            <select id="notif-time" style="
                width: 100%;
                padding: 10px;
                background: #1a1a1a;
                color: #FFD700;
                border: 2px solid #FFD700;
                border-radius: 6px;
                margin-bottom: 15px;
            ">
                <option value="6">6:00 AM</option>
                <option value="7">7:00 AM</option>
                <option value="8">8:00 AM</option>
                <option value="9" selected>9:00 AM</option>
            </select>
            <button id="set-notif-btn" style="
                width: 100%;
                padding: 12px;
                background: #FFD700;
                color: #000;
                border: none;
                border-radius: 6px;
                font-weight: bold;
                cursor: pointer;
            ">Set Daily WOD Time</button>
        `;
        
        document.body.appendChild(prompt);
        
        // Add event listener properly
        document.getElementById('set-notif-btn').addEventListener('click', async () => {
            const time = document.getElementById('notif-time').value;
            localStorage.setItem('tg_notificationTime', time);
            
            const permission = await Notification.requestPermission();
            
            if (permission === 'granted') {
                prompt.innerHTML = `
                    <h3 style="color: #FFD700;">✅ Set!</h3>
                    <p style="color: #fff;">Daily WOD at ${time}:00 AM</p>
                `;
                
                // Schedule notification
                this.scheduleNotification(time);
            } else {
                prompt.innerHTML = `
                    <p style="color: #FFD700;">Enable notifications in settings</p>
                `;
            }
            
            setTimeout(() => prompt.remove(), 3000);
        });
    }
    
    scheduleNotification(hour) {
        const now = new Date();
        const scheduled = new Date();
        scheduled.setHours(parseInt(hour), 0, 0, 0);
        
        if (scheduled <= now) {
            scheduled.setDate(scheduled.getDate() + 1);
        }
        
        const timeout = scheduled - now;
        
        setTimeout(() => {
            if (Notification.permission === 'granted') {
                new Notification('🧠 Leadership WOD', {
                    body: `Day ${this.state.currentDay}: Your daily challenge is ready.`,
                    icon: '/icon-192.png'
                });
            }
            this.scheduleNotification(hour);
        }, timeout);
    }
    
    updateUserDisplay() {
        document.getElementById('userName').textContent = this.userData.name;
        document.getElementById('userDay').textContent = `Day ${this.state.currentDay}`;
    }
    
    setupEventListeners() {
        const input = document.getElementById('messageInput');
        const button = document.getElementById('sendButton');
        
        input.addEventListener('input', () => {
            button.disabled = !input.value.trim() || this.state.isProcessing;
            input.style.height = 'auto';
            input.style.height = Math.min(input.scrollHeight, 120) + 'px';
        });
        
        input.addEventListener('keydown', (e) => {
            if (e.key === 'Enter' && !e.shiftKey) {
                e.preventDefault();
                this.sendMessage();
            }
        });
        
        button.addEventListener('click', () => this.sendMessage());
    }
    
    loadMessages() {
        const history = localStorage.getItem('tg_messageHistory');
        if (history) {
            const messages = JSON.parse(history);
            messages.slice(-10).forEach(msg => {
                this.addMessage(msg.text, msg.sender, false);
            });
        }
    }
    
    async sendMessage() {
        const input = document.getElementById('messageInput');
        const message = input.value.trim();
        if (!message || this.state.isProcessing) return;
        
        this.state.isProcessing = true;
        document.getElementById('sendButton').disabled = true;
        
        this.addMessage(message, 'user');
        input.value = '';
        input.style.height = 'auto';
        
        this.showTyping();
        
        try {
            const response = await fetch('/api/chat', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    message,
                    userId: this.state.userId,
                    conversationId: this.state.conversationId,
                    userName: this.userData.name,
                    businessName: this.userData.businessName,
                    currentDay: this.state.currentDay
                })
            });
            
            const data = await response.json();
            this.hideTyping();
            
            this.addMessage(data.message, 'genie');
            
            if (data.conversationId) {
                this.state.conversationId = data.conversationId;
                localStorage.setItem('tg_conversationId', data.conversationId);
            }
            
        } catch (error) {
            this.hideTyping();
            this.addMessage('Connection issue. Try again.', 'genie');
        } finally {
            this.state.isProcessing = false;
            document.getElementById('sendButton').disabled = false;
            input.focus();
        }
    }
    
    addMessage(text, sender, save = true) {
        const container = document.getElementById('chatContainer');
        const messageDiv = document.createElement('div');
        messageDiv.className = `message ${sender}`;
        
        const bubble = document.createElement('div');
        bubble.className = 'message-bubble';
        bubble.innerHTML = text.replace(/\*\*(.*?)\*\*/g, '<strong>$1</strong>');
        
        messageDiv.appendChild(bubble);
        container.insertBefore(messageDiv, document.getElementById('typingIndicator'));
        
        if (save) {
            const history = JSON.parse(localStorage.getItem('tg_messageHistory') || '[]');
            history.push({ text, sender, timestamp: new Date().toISOString() });
            if (history.length > 100) history.shift();
            localStorage.setItem('tg_messageHistory', JSON.stringify(history));
        }
        
        this.scrollToBottom();
    }
    
    showTyping() {
        document.getElementById('typingIndicator').classList.add('active');
        this.scrollToBottom();
    }
    
    hideTyping() {
        document.getElementById('typingIndicator').classList.remove('active');
    }
    
    scrollToBottom() {
        const container = document.getElementById('chatContainer');
        requestAnimationFrame(() => {
            container.scrollTop = container.scrollHeight;
        });
    }
}

// Initialize when ready
if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', () => {
        window.app = new TinkerGenieApp();
    });
} else {
    window.app = new TinkerGenieApp();
}
