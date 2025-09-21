import { signalRService } from './signalRService';

export interface ChatResponse {
  response: string;
  conversationId?: string;
  isError?: boolean;
  isDailyPrompt?: boolean;
  dayNumber?: number;
  sessionType?: string;
}

class ChatService {
  private sessionContext: any = {};

  async sendMessage(message: string, conversationId?: string): Promise<ChatResponse> {
    try {
      // Use SignalR for real-time messaging
      if (signalRService.isConnected()) {
        await signalRService.sendMessage(message, conversationId);
        
        // Return immediately - response will come via ReceiveMessage handler
        return {
          response: 'Message sent via SignalR',
          conversationId: conversationId
        };
      } else {
        // Fallback to REST if SignalR disconnected
        const response = await fetch('/api/chat/send', {
          method: 'POST',
          headers: {
            'Content-Type': 'application/json',
            'Authorization': `Bearer ${localStorage.getItem('token')}`
          },
          body: JSON.stringify({
            message,
            userId: localStorage.getItem('userId'),
            conversationId
          })
        });
        
        return await response.json();
      }
    } catch (error) {
      console.error('Chat service error:', error);
      throw error;
    }
  }

  async checkDailyPromptStatus() {
    const response = await fetch('/api/genie/current-day', {
      headers: {
        'Authorization': `Bearer ${localStorage.getItem('token')}`
      }
    });
    return await response.json();
  }

  setSessionContext(context: any) {
    this.sessionContext = context;
  }

  getSessionContext() {
    return this.sessionContext;
  }
}

export const chatService = new ChatService();