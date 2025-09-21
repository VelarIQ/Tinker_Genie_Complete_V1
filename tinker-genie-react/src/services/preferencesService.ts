import { apiService } from './apiService';

export interface UserPreferences {
  userId: string;
  communicationStyle?: string;
  notificationTime?: string;
  notificationsEnabled: boolean;
  dailyPromptTime?: string;
  timezone?: string;
  language?: string;
  theme?: 'light' | 'dark';
  onboardingCompleted?: boolean;
}

class PreferencesService {
  private cachedPreferences: UserPreferences | null = null;

  /**
   * Get user preferences - checks cache first, then API
   */
  async getPreferences(): Promise<UserPreferences> {
    try {
      const userId = localStorage.getItem('userId');
      if (!userId) throw new Error('No user ID found');
      
      const preferences = await apiService.get<UserPreferences>(`/preferences/${userId}`);
      this.cachedPreferences = preferences;
      localStorage.setItem('tinker_preferences', JSON.stringify(preferences));
      return preferences;
    } catch (error) {
      // If API fails, check cache
      const cached = this.getCachedPreferences();
      if (cached) return cached;
      
      // Return defaults if no cache
      return { 
        userId: localStorage.getItem('userId') || '', 
        notificationsEnabled: false 
      };
    }
  }

  /**
   * Update preferences - saves to backend and cache
   */
  async updatePreferences(preferences: Partial<UserPreferences>): Promise<UserPreferences> {
    const userId = localStorage.getItem('userId');
    if (!userId) throw new Error('No user ID found');
    
    const updated = await apiService.put<UserPreferences>(`/preferences/${userId}`, preferences);
    this.cachedPreferences = updated;
    localStorage.setItem('tinker_preferences', JSON.stringify(updated));
    
    // Also save individual preferences for quick access
    if (preferences.communicationStyle) {
      localStorage.setItem('tinker_communication_style', preferences.communicationStyle);
    }
    if (preferences.notificationTime) {
      localStorage.setItem('tinker_notification_time', preferences.notificationTime);
    }
    
    return updated;
  }

  /**
   * Get cached preferences from localStorage
   */
  getCachedPreferences(): UserPreferences | null {
    if (this.cachedPreferences) return this.cachedPreferences;
    
    const stored = localStorage.getItem('tinker_preferences');
    if (stored) {
      try {
        return JSON.parse(stored);
      } catch {
        return null;
      }
    }
    return null;
  }

  /**
   * Check if user has set preferences
   */
  hasPreferencesSet(): boolean {
    const prefs = this.getCachedPreferences();
    return !!(prefs?.communicationStyle && prefs?.notificationTime);
  }

  /**
   * Clear cached preferences
   */
  clearCache() {
    this.cachedPreferences = null;
    localStorage.removeItem('tinker_preferences');
    localStorage.removeItem('tinker_communication_style');
    localStorage.removeItem('tinker_notification_time');
  }
}

export const preferencesService = new PreferencesService();