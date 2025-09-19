// App fixes for TinkerGenie PWA - Fixed recursive loop
(function() {
    console.log('App fixes loaded - SignalR handler cleanup (fixed)');

    // Private variable to store the actual container
    let _messagesContainer = null;

    // ---- 1. Ensure messagesContainer is ALWAYS available globally ----
    function ensureMessagesContainer() {
        if (!_messagesContainer) {
            _messagesContainer = document.getElementById('messages') || 
                           document.querySelector('[data-messages]') || 
                           document.querySelector('.chat-messages') ||
                           document.getElementById('chatMessages');
            
            if (!_messagesContainer) {
                _messagesContainer = document.createElement('div');
                _messagesContainer.id = 'messages';
                _messagesContainer.setAttribute('role', 'log');
                _messagesContainer.className = 'chat-messages';
                const root = document.getElementById('chat-root') || 
                            document.querySelector('[data-chat-root]') || 
                            document.getElementById('chatContainer') ||
                            document.body;
                root.appendChild(_messagesContainer);
            }
        }
        return _messagesContainer;
    }

    // Set it immediately
    window.messagesContainer = ensureMessagesContainer();

    // ---- 2. Helper functions ----
    function hideTypingIndicator() {
        const indicator = document.getElementById('typingIndicator') || 
                         document.querySelector('.typing-indicator');
        if (indicator) indicator.style.display = 'none';
    }

    function showTypingIndicator() {
        const indicator = document.getElementById('typingIndicator') || 
                         document.querySelector('.typing-indicator');
        if (indicator) indicator.style.display = 'block';
    }

    function renderAssistantMessage(text) {
        if (!text) return;
        
        const container = ensureMessagesContainer();
        const chatMessages = document.getElementById('chatMessages') || container;
        
        const message = document.createElement('div');
        message.className = 'message assistant';
        
        const content = document.createElement('div');
        content.className = 'message-content';
        content.textContent = text;
        
        message.appendChild(content);
        chatMessages.appendChild(message);
        chatMessages.scrollTop = chatMessages.scrollHeight;
    }

    // ---- 3. Clean up any existing SignalR connection handlers ----
    function cleanupExistingHandlers() {
        // Find all possible connection objects
        const connections = [];
        
        // Check window.connection
        if (window.connection) connections.push(window.connection);
        
        // Check TinkerGenieChatbot
        if (window.TinkerGenieChatbot && window.TinkerGenieChatbot.connection) {
            connections.push(window.TinkerGenieChatbot.connection);
        }
        
        // Check any other chatbot instances
        if (window.chatbot && window.chatbot.connection) {
            connections.push(window.chatbot.connection);
        }

        connections.forEach(conn => {
            if (!conn || conn.__cleaned) return;
            conn.__cleaned = true;
            
            console.log('Cleaning up existing SignalR handlers');
            
            // Remove ALL existing handlers
            try { conn.off('ReceiveMessage'); } catch {}
            try { conn.off('receivemessage'); } catch {}
            try { conn.off('ReceiveTypingIndicator'); } catch {}
            try { conn.off('receivetypingindicator'); } catch {}
            try { conn.off('ReceiveDailyPrompt'); } catch {}
            try { conn.off('receivedailyprompt'); } catch {}
            
            // Deep clean internal handler maps if they exist
            if (conn._methods) {
                delete conn._methods['receivemessage'];
                delete conn._methods['receivetypingindicator'];
                delete conn._methods['receivedailyprompt'];
            }
            if (conn._callbacks) {
                delete conn._callbacks['receivemessage'];
                delete conn._callbacks['receivetypingindicator'];
                delete conn._callbacks['receivedailyprompt'];
            }
            
            // Re-register only the correct handlers
            conn.on('ReceiveMessage', (data) => {
                try {
                    window.messagesContainer = ensureMessagesContainer();
                    hideTypingIndicator();
                    
                    // Handle various payload formats
                    let message = '';
                    if (typeof data === 'string') {
                        message = data;
                    } else if (data && typeof data === 'object') {
                        message = data.message || data.text || data.content || '';
                    }
                    
                    if (message) {
                        renderAssistantMessage(message);
                    }
                } catch (e) {
                    console.error('Error in ReceiveMessage handler:', e);
                }
            });
            
            conn.on('ReceiveTypingIndicator', () => {
                try {
                    showTypingIndicator();
                } catch (e) {
                    console.error('Error in ReceiveTypingIndicator handler:', e);
                }
            });
            
            conn.on('ReceiveDailyPrompt', (data) => {
                try {
                    window.messagesContainer = ensureMessagesContainer();
                    
                    let prompt = '';
                    if (typeof data === 'string') {
                        prompt = data;
                    } else if (data && typeof data === 'object') {
                        prompt = data.prompt || data.message || data.text || '';
                    }
                    
                    if (prompt) {
                        renderAssistantMessage(prompt);
                    }
                } catch (e) {
                    console.error('Error in ReceiveDailyPrompt handler:', e);
                }
            });
            
            console.log('SignalR handlers cleaned and re-registered');
        });
    }

    // ---- 4. Intercept SignalR HubConnection creation ----
    if (window.signalR && window.signalR.HubConnection) {
        const originalOn = window.signalR.HubConnection.prototype.on;
        window.signalR.HubConnection.prototype.on = function(methodName, newMethod) {
            const name = (methodName || '').toString().toLowerCase();
            
            // Block lowercase variants completely
            if (name === 'receivemessage' || name === 'receivetypingindicator' || name === 'receivedailyprompt') {
                console.log('Blocked lowercase handler registration:', name);
                // Wrap the handler to ensure messagesContainer exists
                const wrappedHandler = function(...args) {
                    try {
                        window.messagesContainer = ensureMessagesContainer();
                        return newMethod.apply(this, args);
                    } catch (e) {
                        console.warn('Suppressed error in legacy handler:', name, e);
                    }
                };
                return originalOn.call(this, methodName, wrappedHandler);
            }
            
            return originalOn.call(this, methodName, newMethod);
        };
    }

    // ---- 5. Watch for connection creation and clean it up ----
    let checkInterval = setInterval(() => {
        if (window.connection || (window.TinkerGenieChatbot && window.TinkerGenieChatbot.connection)) {
            clearInterval(checkInterval);
            setTimeout(() => {
                cleanupExistingHandlers();
            }, 100); // Give it a moment to register handlers
        }
    }, 50);

    // Stop checking after 10 seconds
    setTimeout(() => clearInterval(checkInterval), 10000);

    // ---- 6. Also clean up on DOM ready ----
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', () => {
            window.messagesContainer = ensureMessagesContainer();
            cleanupExistingHandlers();
        });
    } else {
        window.messagesContainer = ensureMessagesContainer();
        cleanupExistingHandlers();
    }

    // ---- 7. Expose utility functions globally ----
    window.TinkerGenieChatbot = window.TinkerGenieChatbot || {};
    window.TinkerGenieChatbot.showTypingIndicator = showTypingIndicator;
    window.TinkerGenieChatbot.hideTypingIndicator = hideTypingIndicator;
    window.TinkerGenieChatbot.renderAssistantMessage = renderAssistantMessage;
    window.TinkerGenieChatbot.ensureMessagesContainer = ensureMessagesContainer;

    console.log('App fixes complete - watching for SignalR connections');
})();
