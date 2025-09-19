class NotificationManager {
    constructor() {
        this.userId = localStorage.getItem('tg_userId');
        this.init();
    }
    
    async init() {
        const notificationTime = localStorage.getItem('tg_notificationTime');
        
        if (!notificationTime) {
            // First visit - ask when they want reminders
            setTimeout(() => this.showTimeSelector(), 10000);
        } else {
            this.scheduleAt(notificationTime);
        }
    }
    
    showTimeSelector() {
        const prompt = document.createElement('div');
        prompt.style.cssText = `
            position: fixed;
            bottom: 20px;
            left: 50%;
            transform: translateX(-50%);
            background: #000;
            border: 3px solid #FFD700;
            border-radius: 12px;
            padding: 24px;
            box-shadow: 0 8px 32px rgba(0,0,0,0.5);
            z-index: 10000;
            max-width: 90%;
            width: 380px;
        `;
        
        prompt.innerHTML = `
            <h3 style="color: #FFD700; margin: 0 0 12px 0; font-size: 20px;">
                ⏰ Daily Leadership Reminder
            </h3>
            <p style="color: #fff; margin: 0 0 20px 0;">
                Growth happens through daily action. When should we remind you?
            </p>
            <select id="notification-time" style="
                width: 100%;
                padding: 10px;
                background: #1a1a1a;
                color: #FFD700;
                border: 2px solid #FFD700;
                border-radius: 6px;
                font-size: 16px;
                margin-bottom: 16px;
            ">
                <option value="6">6:00 AM - Early Bird</option>
                <option value="7">7:00 AM - Morning Focus</option>
                <option value="8">8:00 AM - Pre-Work</option>
                <option value="9" selected>9:00 AM - Start Strong</option>
                <option value="10">10:00 AM - Mid-Morning</option>
                <option value="14">2:00 PM - Afternoon Push</option>
                <option value="20">8:00 PM - Evening Review</option>
            </select>
            <button onclick="notificationManager.setTime()" style="
                width: 100%;
                padding: 12px;
                background: #FFD700;
                color: #000;
                border: none;
                border-radius: 6px;
                font-weight: 700;
                font-size: 16px;
                cursor: pointer;
            ">Set Daily Reminder</button>
        `;
        
        document.body.appendChild(prompt);
        window.notificationPrompt = prompt;
    }
    
    async setTime() {
        const time = document.getElementById('notification-time').value;
        localStorage.setItem('tg_notificationTime', time);
        
        const permission = await Notification.requestPermission();
        
        if (permission === 'granted') {
            this.scheduleAt(time);
            
            window.notificationPrompt.innerHTML = `
                <h3 style="color: #FFD700;">✅ You're Set!</h3>
                <p style="color: #fff;">Daily reminder scheduled for ${time}:00 AM</p>
            `;
            
            setTimeout(() => window.notificationPrompt.remove(), 3000);
        } else {
            window.notificationPrompt.innerHTML = `
                <p style="color: #FFD700;">Please enable notifications in your browser settings.</p>
            `;
        }
    }
    
    scheduleAt(hour) {
        const now = new Date();
        const scheduled = new Date();
        scheduled.setHours(parseInt(hour), 0, 0, 0);
        
        if (scheduled <= now) {
            scheduled.setDate(scheduled.getDate() + 1);
        }
        
        const timeout = scheduled - now;
        
        setTimeout(() => {
            this.showNotification();
            this.scheduleAt(hour); // Reschedule for tomorrow
        }, timeout);
    }
    
    showNotification() {
        if (Notification.permission === 'granted') {
            new Notification('🧠 Tinker Genie - Time to Lead', {
                body: 'Your daily leadership challenge is ready. Take action now.',
                icon: '/icon-192.png',
                badge: '/icon-96.png',
                requireInteraction: true
            });
        }
    }
}

window.notificationManager = new NotificationManager();
