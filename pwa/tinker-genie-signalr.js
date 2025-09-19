// TinkerGenie PWA - WebSocket Connection Handler
// This shows how to handle the typing indicator on the client side

class TinkerGenieChat {
    constructor() {
        this.connection = null;
        this.typingIndicator = null;
    }

    async connect() {
        // Create SignalR connection
        this.connection = new signalR.HubConnectionBuilder()
            .withUrl("/chatHub", {
                accessTokenFactory: () => this.getAuthToken()
            })
            .withAutomaticReconnect()
            .build();

        // Handle typing indicator
        this.connection.on("ReceiveTypingIndicator", (data) => {
            this.handleTypingIndicator(data);
        });

        // Handle messages
        this.connection.on("ReceiveMessage", (data) => {
            this.handleMessage(data);
        });

        await this.connection.start();
        console.log("Connected to TinkerGenie");
    }

    handleTypingIndicator(data) {
        const typingElement = document.getElementById('typing-indicator');
        
        if (data.isTyping) {
            // Show typing indicator
            typingElement.innerHTML = `
                <div class="typing-indicator active">
                    <span class="typing-text">${data.message}</span>
                    <span class="typing-dots">
                        <span></span>
                        <span></span>
                        <span></span>
                    </span>
                </div>
            `;
            typingElement.style.display = 'block';
        } else {
            // Hide typing indicator
            typingElement.style.display = 'none';
            typingElement.innerHTML = '';
        }
    }

    handleMessage(data) {
        // Hide typing indicator when message arrives
        const typingElement = document.getElementById('typing-indicator');
        typingElement.style.display = 'none';

        // Add message to chat
        this.addMessageToChat(data.message, 'tinkergenie');
        
        // Handle special message types
        if (data.isDailyPrompt) {
            this.updateDayCounter(data.dayNumber);
        }
        
        if (data.showOptions) {
            this.showConversationOptions(data.options);
        }
        
        if (data.isNewThread) {
            this.addNewThreadToSidebar(data.conversationId, data.threadType);
        }
    }

    async sendMessage(message) {
        // Call the WebSocket hub
        await this.connection.invoke("SendMessage", message);
    }

    addMessageToChat(message, sender) {
        const chatContainer = document.getElementById('chat-messages');
        const messageDiv = document.createElement('div');
        messageDiv.className = `message ${sender}`;
        
        // Format message (could have markdown)
        const formattedMessage = this.formatMessage(message);
        messageDiv.innerHTML = `
            <div class="message-bubble">
                <div class="message-content">${formattedMessage}</div>
                <div class="message-time">${new Date().toLocaleTimeString()}</div>
            </div>
        `;
        
        chatContainer.appendChild(messageDiv);
        chatContainer.scrollTop = chatContainer.scrollHeight;
    }

    formatMessage(message) {
        // Convert markdown-style formatting
        return message
            .replace(/\*\*(.*?)\*\*/g, '<strong>$1</strong>')
            .replace(/\n/g, '<br>')
            .replace(/•/g, '<span class="bullet">•</span>');
    }

    showConversationOptions(options) {
        if (!options) return;
        
        const optionsContainer = document.getElementById('conversation-options');
        optionsContainer.innerHTML = '';
        
        options.forEach(option => {
            const button = document.createElement('button');
            button.className = 'option-button';
            button.textContent = option.description;
            button.onclick = () => this.sendMessage(option.phrase);
            optionsContainer.appendChild(button);
        });
    }

    updateDayCounter(dayNumber) {
        const dayElement = document.getElementById('day-counter');
        if (dayElement && dayNumber) {
            dayElement.textContent = `Day ${dayNumber}`;
        }
    }

    addNewThreadToSidebar(conversationId, threadType) {
        const sidebar = document.getElementById('conversation-threads');
        const threadDiv = document.createElement('div');
        threadDiv.className = `thread-item ${threadType}`;
        threadDiv.dataset.conversationId = conversationId;
        
        const icon = this.getThreadIcon(threadType);
        const title = this.getThreadTitle(threadType);
        
        threadDiv.innerHTML = `
            <span class="thread-icon">${icon}</span>
            <span class="thread-title">${title}</span>
            <span class="thread-time">${new Date().toLocaleTimeString()}</span>
        `;
        
        sidebar.insertBefore(threadDiv, sidebar.firstChild);
    }

    getThreadIcon(threadType) {
        const icons = {
            'daily_prompt': '📝',
            'burning_fire': '🔥',
            'tinker_level': '🔧',
            'general_chat': '💬',
            'completion': '✅'
        };
        return icons[threadType] || '💬';
    }

    getThreadTitle(threadType) {
        const titles = {
            'daily_prompt': 'Daily Reflection',
            'burning_fire': 'Burning Fire',
            'tinker_level': 'Tinker Issue',
            'general_chat': 'Chat',
            'completion': 'Completed'
        };
        return titles[threadType] || 'Conversation';
    }

    getAuthToken() {
        // Get JWT token from storage
        return localStorage.getItem('authToken') || '';
    }
}

// CSS for typing indicator animation
const typingIndicatorStyles = `
<style>
.typing-indicator {
    display: flex;
    align-items: center;
    padding: 12px 16px;
    background: #f0f0f0;
    border-radius: 18px;
    margin: 8px 0;
    max-width: 200px;
}

.typing-indicator.active {
    animation: fadeIn 0.3s ease-in;
}

.typing-text {
    color: #666;
    font-size: 14px;
    margin-right: 8px;
    font-style: italic;
}

.typing-dots {
    display: flex;
    align-items: center;
    gap: 4px;
}

.typing-dots span {
    width: 8px;
    height: 8px;
    border-radius: 50%;
    background-color: #999;
    animation: typing 1.4s infinite ease-in-out;
}

.typing-dots span:nth-child(1) {
    animation-delay: -0.32s;
}

.typing-dots span:nth-child(2) {
    animation-delay: -0.16s;
}

@keyframes typing {
    0%, 80%, 100% {
        transform: scale(0.8);
        opacity: 0.5;
    }
    40% {
        transform: scale(1);
        opacity: 1;
    }
}

@keyframes fadeIn {
    from {
        opacity: 0;
        transform: translateY(-10px);
    }
    to {
        opacity: 1;
        transform: translateY(0);
    }
}

.message-bubble {
    padding: 12px 16px;
    border-radius: 18px;
    max-width: 70%;
    word-wrap: break-word;
}

.message.tinkergenie .message-bubble {
    background: #007bff;
    color: white;
    margin-right: auto;
}

.message.user .message-bubble {
    background: #e9ecef;
    color: #333;
    margin-left: auto;
}

.option-button {
    padding: 8px 16px;
    margin: 4px;
    border: 1px solid #007bff;
    background: white;
    color: #007bff;
    border-radius: 20px;
    cursor: pointer;
    transition: all 0.3s ease;
}

.option-button:hover {
    background: #007bff;
    color: white;
}
</style>
`;

// Initialize on page load
document.addEventListener('DOMContentLoaded', () => {
    // Add styles to page
    document.head.insertAdjacentHTML('beforeend', typingIndicatorStyles);
    
    // Create chat instance
    const chat = new TinkerGenieChat();
    
    // Connect to WebSocket
    chat.connect().catch(error => {
        console.error('Failed to connect:', error);
    });
    
    // Handle send button
    const sendButton = document.getElementById('send-button');
    const messageInput = document.getElementById('message-input');
    
    if (sendButton && messageInput) {
        sendButton.addEventListener('click', () => {
            const message = messageInput.value.trim();
            if (message) {
                chat.sendMessage(message);
                chat.addMessageToChat(message, 'user');
                messageInput.value = '';
            }
        });
        
        // Send on Enter key
        messageInput.addEventListener('keypress', (e) => {
            if (e.key === 'Enter' && !e.shiftKey) {
                e.preventDefault();
                sendButton.click();
            }
        });
    }
});

// Export for use in other modules
window.TinkerGenieChat = TinkerGenieChat;
