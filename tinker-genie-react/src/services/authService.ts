import { apiService } from './apiService';
import { User } from '../store/authSlice';

interface LoginResponse {
  token: string;
  user: User;
}

interface RegisterData {
  email: string;
  password: string;
  firstName: string;
  lastName: string;
  businessName?: string;
}

class AuthService {
  async login(credentials: { email?: string; username?: string; password: string }): Promise<LoginResponse> {
    const payload = {
      username: credentials.username ?? credentials.email,
      password: credentials.password,
    };

    const res = await apiService.post<any>('/auth/login', payload);

    const token: string = res?.token || res?.accessToken || '';
    const user: User = {
      id: res?.userId || res?.id || '',
      email: res?.email || payload.username || '',
      firstName: res?.name || res?.firstName || '',
      lastName: res?.lastName || '',
      businessName: res?.businessName,
      role: 'user',
      name: res?.name || undefined,
    };

    return { token, user };
  }

  async googleLogin(googleToken: string): Promise<LoginResponse> {
    return apiService.post<LoginResponse>('/auth/google', { token: googleToken });
  }

  async register(data: RegisterData): Promise<LoginResponse> {
    return apiService.post<LoginResponse>('/auth/register', data);
  }

  async logout(): Promise<void> {
    return apiService.post('/auth/logout');
  }

  async validateToken(_token: string): Promise<{ user: User; valid: boolean }> {
    // Use protected endpoint to validate and fetch the current user
    const me = await apiService.get<any>('/user/me');
    const user: User = {
      id: me?.userId || me?.id || '',
      email: me?.email || '',
      firstName: me?.name || me?.firstName || '',
      lastName: me?.lastName || '',
      businessName: me?.businessName,
      role: 'user',
      name: me?.name || undefined,
    };
    return { user, valid: true };
  }

  async refreshToken(): Promise<{ token: string }> {
    return apiService.post('/auth/refresh');
  }

  async forgotPassword(email: string): Promise<{ message: string }> {
    return apiService.post('/auth/forgot-password', { email });
  }

  async resetPassword(token: string, newPassword: string): Promise<{ message: string }> {
    return apiService.post('/auth/reset-password', { token, newPassword });
  }

  async updateProfile(profileData: Partial<User>): Promise<{ user: User }> {
    return apiService.put('/auth/profile', profileData);
  }

  async changePassword(currentPassword: string, newPassword: string): Promise<{ message: string }> {
    return apiService.post('/auth/change-password', { currentPassword, newPassword });
  }

  async verifyEmail(token: string): Promise<{ message: string }> {
    return apiService.post('/auth/verify-email', { token });
  }

  async resendVerificationEmail(): Promise<{ message: string }> {
    return apiService.post('/auth/resend-verification');
  }
}

export const authService = new AuthService();
