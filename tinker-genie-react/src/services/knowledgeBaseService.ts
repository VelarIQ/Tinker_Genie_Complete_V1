import { apiService } from './apiService';
import { ConversationType } from './conversationService';

export interface KnowledgeResult {
  id: string;
  content: string;
  relevance: number;
  category: string;
  metadata?: any;
  source?: string;
  tags?: string[];
}

export interface WODResult {
  word: string;
  definition: string;
  example: string;
  category: string;
}

class KnowledgeBaseService {
  /**
   * Search knowledge base for relevant content
   */
  async searchContent(
    query: string, 
    conversationType?: ConversationType
  ): Promise<KnowledgeResult[]> {
    try {
      const response = await apiService.post<KnowledgeResult[]>(
        '/curriculum/search',
        {
          query,
          conversationType,
          limit: 5
        }
      );
      return response;
    } catch (error) {
      console.error('Knowledge base search failed:', error);
      return [];
    }
  }

  /**
   * Get Word of the Day (WOD) content
   */
  async getWOD(date?: string): Promise<WODResult | null> {
    try {
      const response = await apiService.get<WODResult>(
        '/wod/today',
        { date: date || new Date().toISOString().split('T')[0] }
      );
      return response;
    } catch (error) {
      console.error('Failed to get WOD:', error);
      return null;
    }
  }

  /**
   * Search for burning fires solutions
   */
  async searchBurningFiresSolutions(issue: string): Promise<KnowledgeResult[]> {
    try {
      const response = await apiService.post<KnowledgeResult[]>(
        '/curriculum/burning-fires',
        {
          issue,
          limit: 3
        }
      );
      return response;
    } catch (error) {
      console.error('Burning fires search failed:', error);
      return [];
    }
  }

  /**
   * Search for leadership content
   */
  async searchLeadershipContent(topic: string): Promise<KnowledgeResult[]> {
    try {
      const response = await apiService.post<KnowledgeResult[]>(
        '/curriculum/leadership',
        {
          topic,
          limit: 5
        }
      );
      return response;
    } catch (error) {
      console.error('Leadership content search failed:', error);
      return [];
    }
  }

  /**
   * Get daily leadership prompt
   */
  async getDailyPrompt(dayNumber: number): Promise<{
    prompt: string;
    context: string;
    resources?: string[];
  } | null> {
    try {
      const response = await apiService.get<{
        prompt: string;
        context: string;
        resources?: string[];
      }>(`/curriculum/today/${dayNumber}`);
      return response;
    } catch (error) {
      console.error('Failed to get daily prompt:', error);
      return null;
    }
  }

  /**
   * Search TwoBrain.app for relevant links
   */
  async searchTwoBrainLinks(query: string): Promise<{
    title: string;
    url: string;
    description: string;
  }[]> {
    try {
      const response = await apiService.post<{
        title: string;
        url: string;
        description: string;
      }[]>(
        '/curriculum/resources',
        { query }
      );
      return response;
    } catch (error) {
      console.error('TwoBrain links search failed:', error);
      return [];
    }
  }

  /**
   * Get contextual suggestions based on conversation
   */
  async getContextualSuggestions(
    messages: string[],
    conversationType: ConversationType
  ): Promise<string[]> {
    try {
      const response = await apiService.post<string[]>(
        '/curriculum/suggestions',
        {
          messages: messages.slice(-5), // Last 5 messages for context
          conversationType
        }
      );
      return response;
    } catch (error) {
      console.error('Failed to get suggestions:', error);
      return [];
    }
  }

  /**
   * Index new content into knowledge base
   */
  async indexContent(content: {
    text: string;
    category: string;
    tags?: string[];
    metadata?: any;
  }): Promise<boolean> {
    try {
      await apiService.post('/curriculum/index', content);
      return true;
    } catch (error) {
      console.error('Failed to index content:', error);
      return false;
    }
  }
}

export const knowledgeBaseService = new KnowledgeBaseService();