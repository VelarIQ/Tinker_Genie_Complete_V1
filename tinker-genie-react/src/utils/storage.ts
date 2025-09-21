import { User } from '../store/authSlice';

const TOKEN_KEY = 'tinker_genie_token';
const USER_KEY = 'tinker_genie_user';
const THEME_KEY = 'tinker_genie_theme';

class Storage {
  // Token management
  getToken(): string | null {
    try {
      // Prefer session token when present (non-remembered sessions)
      const sessionToken = sessionStorage.getItem(TOKEN_KEY);
      if (sessionToken) return sessionToken;
      return localStorage.getItem(TOKEN_KEY);
    } catch (error) {
      console.error('Error getting token from storage:', error);
      return null;
    }
  }

  setToken(token: string): void {
    try {
      localStorage.setItem(TOKEN_KEY, token);
    } catch (error) {
      console.error('Error setting token in storage:', error);
    }
  }

  setTokenRemembered(token: string, remember: boolean): void {
    try {
      if (remember) {
        // Persist across sessions
        sessionStorage.removeItem(TOKEN_KEY);
        localStorage.setItem(TOKEN_KEY, token);
      } else {
        // Session-only
        localStorage.removeItem(TOKEN_KEY);
        sessionStorage.setItem(TOKEN_KEY, token);
      }
    } catch (error) {
      console.error('Error setting token with remember flag:', error);
    }
  }

  removeToken(): void {
    try {
      sessionStorage.removeItem(TOKEN_KEY);
      localStorage.removeItem(TOKEN_KEY);
    } catch (error) {
      console.error('Error removing token from storage:', error);
    }
  }

  // User management
  getUser(): User | null {
    try {
      const userStr = localStorage.getItem(USER_KEY);
      return userStr ? JSON.parse(userStr) : null;
    } catch (error) {
      console.error('Error getting user from storage:', error);
      return null;
    }
  }

  setUser(user: User): void {
    try {
      localStorage.setItem(USER_KEY, JSON.stringify(user));
    } catch (error) {
      console.error('Error setting user in storage:', error);
    }
  }

  removeUser(): void {
    try {
      localStorage.removeItem(USER_KEY);
    } catch (error) {
      console.error('Error removing user from storage:', error);
    }
  }

  // Theme management
  getTheme(): 'light' | 'dark' {
    try {
      const theme = localStorage.getItem(THEME_KEY);
      return theme === 'dark' ? 'dark' : 'light';
    } catch (error) {
      console.error('Error getting theme from storage:', error);
      return 'light';
    }
  }

  setTheme(theme: 'light' | 'dark'): void {
    try {
      localStorage.setItem(THEME_KEY, theme);
      if (theme === 'dark') {
        document.documentElement.classList.add('dark');
      } else {
        document.documentElement.classList.remove('dark');
      }
    } catch (error) {
      console.error('Error setting theme in storage:', error);
    }
  }

  // Clear all auth data
  clearAuth(): void {
    this.removeToken();
    this.removeUser();
  }

  // Clear all storage
  clearAll(): void {
    try {
      localStorage.clear();
    } catch (error) {
      console.error('Error clearing storage:', error);
    }
  }

  // Session storage for temporary data
  getSessionItem(key: string): any {
    try {
      const item = sessionStorage.getItem(key);
      return item ? JSON.parse(item) : null;
    } catch (error) {
      console.error(`Error getting session item ${key}:`, error);
      return null;
    }
  }

  setSessionItem(key: string, value: any): void {
    try {
      sessionStorage.setItem(key, JSON.stringify(value));
    } catch (error) {
      console.error(`Error setting session item ${key}:`, error);
    }
  }

  removeSessionItem(key: string): void {
    try {
      sessionStorage.removeItem(key);
    } catch (error) {
      console.error(`Error removing session item ${key}:`, error);
    }
  }
}

export const storage = new Storage();
