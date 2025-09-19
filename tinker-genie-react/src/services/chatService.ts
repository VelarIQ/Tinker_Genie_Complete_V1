import { apiService } from './apiService';
import { ConversationType } from './conversationService';

export interface Message {
  id: string;
  content: string;
  sender: 'user' | 'assistant';
  timestamp: Date;
  conversationId?: string;
}

export interface ChatResponse {
  response: string;
  conversationId: string;
  messageId?: string;
  isDailyPrompt?: boolean;
  isDailyPromptComplete?: boolean;
  dayNumber?: number;
  promptTitle?: string;
  sessionType?: string;
  isSessionEnd?: boolean;
}

export interface SessionResponse {
  response: string;
  conversationId: string;
  isDailyPrompt: boolean;
  dayNumber: number;
  promptTitle: string;
  sessionType: string;
}

export interface NewChatResponse {
  success: boolean;
  conversationId: string;
  message: string;
  sessionCleared: boolean;
  timestamp: string;
}

export interface ConversationData {
  id: string;
  messages: Message[];
  title: string;
  createdAt: string;
  updatedAt: string;
}

export interface DailyPromptStatus {
  isComplete: boolean;
  dayNumber: number;
  completedAt?: string;
}

class ChatService {
  private currentSessionType: string | null = null;
  private currentConversationId: string | null = null;
  private dailyPromptCompleted: boolean = false;

  /**
   * Set session context for maintaining conversation state
   */
  setSessionContext(type: string | null, conversationId: string | null) {
    this.currentSessionType = type;
    this.currentConversationId = conversationId;
    
    if (type) sessionStorage.setItem('current_session_type', type);
    if (conversationId) sessionStorage.setItem('current_conversation_id', conversationId);
  }

  /**
   * Get current session context
   */
  getSessionContext() {
    return {
      sessionType: this.currentSessionType || sessionStorage.getItem('current_session_type'),
      conversationId: this.currentConversationId || sessionStorage.getItem('current_conversation_id')
    };
  }

  /**
   * Clear session context
   */
  clearSessionContext() {
    this.currentSessionType = null;
    this.currentConversationId = null;
    this.dailyPromptCompleted = false;
    sessionStorage.removeItem('current_session_type');
    sessionStorage.removeItem('current_conversation_id');
  }

  /**
   * Send a message with context preservation
   */
  async sendMessage(message: string, conversationId?: string, sessionType?: string): Promise<ChatResponse> {
    const context = this.getSessionContext();
    const actualConversationId = conversationId || context.conversationId;
    const actualSessionType = sessionType || context.sessionType;
    
    const response = await apiService.post<ChatResponse>('/chat/message', {
      message,
      conversationId: actualConversationId,
      sessionType: actualSessionType,
    });

    // Update context with response data
    if (response.conversationId) {
      this.currentConversationId = response.conversationId;
    }
    if (response.sessionType) {
      this.currentSessionType = response.sessionType;
    }
    if (response.isDailyPromptComplete) {
      this.dailyPromptCompleted = true;
      sessionStorage.setItem('daily_prompt_completed', 'true');
    }

    return response;
  }

  /**
   * Start a new session (for daily prompts)
   */
  async startSession(): Promise<SessionResponse> {
    const response = await apiService.post<SessionResponse>('/chat/session/start');
    
    // Set session context
    this.setSessionContext('daily_prompt', response.conversationId);
    
    return response;
  }

  /**
   * Start a new chat (clears session)
   */
  async startNewChat(): Promise<NewChatResponse> {
    const response = await apiService.post<NewChatResponse>('/chat/new');
    
    // Clear session context
    this.clearSessionContext();
    
    return response;
  }

  /**
   * End current session
   */
  async endSession(): Promise<ChatResponse> {
    const response = await apiService.post<ChatResponse>('/chat/session/end');
    
    // Clear session context
    this.clearSessionContext();
    
    return response;
  }

  /**
   * Get conversation with messages
   */
  async getConversation(conversationId: string): Promise<ConversationData> {
    const response = await apiService.get<ConversationData>(`/conversations/${conversationId}`);
    
    // Set context from conversation data
    if (response.id) {
      this.currentConversationId = response.id;
    }
    
    return response;
  }

  /**
   * Check daily prompt completion status
   */
  async checkDailyPromptStatus(): Promise<DailyPromptStatus> {
    try {
      const status = await apiService.get<DailyPromptStatus>('/chat/daily-prompt/status');
      this.dailyPromptCompleted = status.isComplete;
      if (status.isComplete) {
        sessionStorage.setItem('daily_prompt_completed', 'true');
      }
      return status;
    } catch (error) {
      console.error('Failed to check daily prompt status:', error);
      return { isComplete: false, dayNumber: 1 };
    }
  }

  /**
   * Stream message with real-time updates
   */
  async streamMessage(
    message: string,
    conversationId?: string,
    onChunk?: (chunk: string) => void,
    onComplete?: (fullResponse: string) => void
  ): Promise<void> {
    const context = this.getSessionContext();
    const actualConversationId = conversationId || context.conversationId;
    
    const response = await fetch(`${apiService.baseURL}/chat/stream`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        ...apiService.getAuthHeaders(),
      },
      body: JSON.stringify({
        message,
        conversationId: actualConversationId,
        sessionType: context.sessionType,
      }),
    });

    if (!response.ok) {
      throw new Error('Stream failed');
    }

    const reader = response.body?.getReader();
    if (!reader) {
      throw new Error('No response body');
    }

    const decoder = new TextDecoder();
    let fullResponse = '';

    try {
      while (true) {
        const { done, value } = await reader.read();
        if (done) break;

        const chunk = decoder.decode(value);
        fullResponse += chunk;
        
        if (onChunk) {
          onChunk(chunk);
        }
      }

      if (onComplete) {
        onComplete(fullResponse);
      }
    } finally {
      reader.releaseLock();
    }
  }

  /**
   * Get conversation metadata
   */
  async getConversationMetadata(conversationId: string): Promise<{
    type: ConversationType;
    dayNumber?: number;
    createdAt: string;
    messageCount: number;
  }> {
    return apiService.get(`/conversations/${conversationId}/metadata`);
  }

  /**
   * Mark daily prompt as complete
   */
  async markDailyPromptComplete(dayNumber: number): Promise<void> {
    await apiService.post('/chat/daily-prompt/complete', { dayNumber });
    this.dailyPromptCompleted = true;
    sessionStorage.setItem('daily_prompt_completed', 'true');
  }
}

export const chatService = new ChatService();