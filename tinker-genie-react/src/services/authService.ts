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
  async login(credentials: { email: string; password: string }): Promise<LoginResponse> {
    return apiService.post<LoginResponse>('/auth/login', credentials);
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

  async validateToken(token: string): Promise<{ user: User; valid: boolean }> {
    return apiService.get('/auth/validate', { token });
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
