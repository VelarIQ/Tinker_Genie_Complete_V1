import { apiService } from './apiService';
import { Conversation } from '../store/conversationSlice';

export enum ConversationType {
  GENERAL_CHAT = 'general_chat',
  DAILY_PROMPT = 'daily_prompt',
  BURNING_FIRE = 'burning_fire',
  TINKER_LEVEL = 'tinker_level',
  SESSION_END = 'session_end'
}

// Conversation type triggers (case-insensitive)
export const ConversationTriggers = {
  // Daily prompt triggers
  'daily prompt': ConversationType.DAILY_PROMPT,
  'give me my daily prompt': ConversationType.DAILY_PROMPT,
  "today's prompt": ConversationType.DAILY_PROMPT,
  'leadership prompt': ConversationType.DAILY_PROMPT,
  "talk more about today's prompt": ConversationType.DAILY_PROMPT,
  "talk more about todays prompt": ConversationType.DAILY_PROMPT,
  'more about the prompt': ConversationType.DAILY_PROMPT,
  'START_DAILY_PROMPT': ConversationType.DAILY_PROMPT,
  
  // Burning fires triggers
  'burning fire': ConversationType.BURNING_FIRE,
  'burning fires': ConversationType.BURNING_FIRE,
  'urgent help': ConversationType.BURNING_FIRE,
  'emergency': ConversationType.BURNING_FIRE,
  'crisis': ConversationType.BURNING_FIRE,
  'START_BURNING_FIRES': ConversationType.BURNING_FIRE,
  
  // Other tinker level triggers
  'other tinker level fires': ConversationType.TINKER_LEVEL,
  'other tinker level issues': ConversationType.TINKER_LEVEL,
  'tinker level': ConversationType.TINKER_LEVEL,
  'tinker issue': ConversationType.TINKER_LEVEL,
  'tinker problem': ConversationType.TINKER_LEVEL,
  
  // Session management
  'done for the day': ConversationType.SESSION_END,
  'done for today': ConversationType.SESSION_END,
  "i'm done": ConversationType.SESSION_END,
  'finished': ConversationType.SESSION_END
};

class ConversationService {
  async getConversations(page = 1, limit = 20) {
    return apiService.get<{
      conversations: Conversation[];
      hasMore: boolean;
      page: number;
    }>('/conversations', { page, limit });
  }

  async getRecentConversations() {
    return apiService.get<Conversation[]>('/conversations/recent');
  }

  async getConversation(id: string) {
    return apiService.get<Conversation>(`/conversations/${id}`);
  }

  async deleteConversation(id: string) {
    return apiService.delete(`/conversations/${id}`);
  }

  async updateConversationTitle(id: string, title: string) {
    return apiService.put<Conversation>(`/conversations/${id}`, { title });
  }

  /**
   * Creates a conversation with smart title generation
   */
  async createConversation(
    type: ConversationType, 
    customTitle?: string,
    metadata?: any
  ): Promise<Conversation> {
    const title = customTitle || this.getInitialTitle(type, metadata?.dayNumber);
    
    const response = await apiService.post<Conversation>('/conversations/create', { 
      type, 
      title,
      metadata: {
        conversationType: type,
        dayNumber: metadata?.dayNumber,
        createdAt: new Date().toISOString(),
        ...metadata
      }
    });
    
    return response;
  }

  /**
   * Gets initial title for conversation before we know the content
   */
  getInitialTitle(type: ConversationType, dayNumber?: number): string {
    switch (type) {
      case ConversationType.DAILY_PROMPT:
        return dayNumber ? `Day ${dayNumber} Prompt` : 'Daily Prompt';
      case ConversationType.BURNING_FIRE:
        return '🔥 Burning Fire';
      case ConversationType.TINKER_LEVEL:
        return '🔧 Tinker Level Issue';
      default:
        return `Chat - ${new Date().toLocaleDateString()}`;
    }
  }

  /**
   * Generates smart title based on message content
   */
  generateSmartTitle(type: ConversationType, message: string, metadata?: any): string {
    const topic = this.extractTopicFromMessage(message, type);
    
    switch (type) {
      case ConversationType.DAILY_PROMPT:
        const day = metadata?.dayNumber || 1;
        return `Day ${day} Leadership Prompt`;
        
      case ConversationType.BURNING_FIRE:
        // Extract the core issue from the message
        const fireIssue = this.extractCoreIssue(message);
        return fireIssue ? `🔥 ${fireIssue}` : '🔥 Urgent Issue';
        
      case ConversationType.TINKER_LEVEL:
        // Extract the core challenge from the message
        const tinkerIssue = this.extractCoreIssue(message);
        return tinkerIssue ? `🔧 ${tinkerIssue}` : '🔧 Leadership Challenge';
        
      default:
        return topic && topic !== 'Issue' 
          ? `Chat: ${topic}` 
          : `Chat - ${new Date().toLocaleDateString()}`;
    }
  }

  /**
   * Extracts meaningful topic from message
   */
  extractTopicFromMessage(message: string, _type: ConversationType): string {
    // Remove trigger phrases
    const triggers = [
      'burning fire', 'burning fires', 'urgent', 'emergency', 'crisis',
      'tinker level', 'other tinker level', 'leadership issue',
      'daily prompt', 'leadership prompt'
    ];
    
    let cleanMessage = message.toLowerCase();
    triggers.forEach(trigger => {
      cleanMessage = cleanMessage.replace(trigger, '').trim();
    });
    
    // Remove common starting words
    const commonWords = ['i have', 'we have', 'there is', 'help with', 'need help'];
    commonWords.forEach(word => {
      cleanMessage = cleanMessage.replace(word, '').trim();
    });
    
    // Capitalize first letter
    cleanMessage = cleanMessage.charAt(0).toUpperCase() + cleanMessage.slice(1);
    
    // Limit length
    if (cleanMessage.length > 50) {
      cleanMessage = cleanMessage.substring(0, 47) + '...';
    }
    
    return cleanMessage || 'Issue';
  }

  /**
   * Extracts the core issue/challenge from a message for better titles
   */
  extractCoreIssue(message: string): string {
    // Clean up the message
    let clean = message.toLowerCase().trim();
    
    // Remove filler phrases
    const fillers = [
      "i'm having", "we're having", "there's a", "we have a", "i have a",
      "problem with", "issue with", "challenge with", "trouble with",
      "help me with", "i need help with", "can you help with"
    ];
    
    fillers.forEach(filler => {
      clean = clean.replace(new RegExp(`^${filler}\s*`, 'i'), '');
    });
    
    // Get first sentence or up to 60 chars
    const firstSentence = clean.split(/[.!?]/)[0];
    let result = firstSentence.trim();
    
    // Capitalize and limit length
    result = result.charAt(0).toUpperCase() + result.slice(1);
    
    if (result.length > 40) {
      result = result.substring(0, 37) + '...';
    }
    
    return result || 'Issue';
  }

  /**
   * Updates conversation title based on first meaningful message
   */
  async updateConversationTitleFromContent(
    conversationId: string, 
    firstMessage: string,
    type: ConversationType,
    metadata?: any
  ): Promise<Conversation> {
    const smartTitle = this.generateSmartTitle(type, firstMessage, metadata);
    return this.updateConversationTitle(conversationId, smartTitle);
  }

  /**
   * Detect conversation type from message content
   */
  detectConversationType(message: string): { 
    type: ConversationType; 
    isExplicitChange: boolean;
    triggeredPhrase?: string;
  } {
    const lowerMessage = message.toLowerCase().trim();
    
    for (const [trigger, type] of Object.entries(ConversationTriggers)) {
      if (lowerMessage.includes(trigger.toLowerCase())) {
        return { 
          type: type as ConversationType, 
          isExplicitChange: true,
          triggeredPhrase: trigger
        };
      }
    }
    
    return { 
      type: ConversationType.GENERAL_CHAT, 
      isExplicitChange: false 
    };
  }

  /**
   * Check if message is requesting session end
   */
  isSessionEndRequest(message: string): boolean {
    const lowerMessage = message.toLowerCase();
    const endPhrases = ['done for the day', 'done for today', "i'm done", 'finished'];
    return endPhrases.some(phrase => lowerMessage.includes(phrase));
  }
}

export const conversationService = new ConversationService();