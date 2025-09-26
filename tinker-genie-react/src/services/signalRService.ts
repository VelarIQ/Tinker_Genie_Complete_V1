import * as signalR from '@microsoft/signalr';
import { store } from '../store';
import { addMessage, setTyping, updateStreamingMessage, setDailyPromptComplete } from '../store/chatSlice';
import { updateConnectionStatus } from '../store/uiSlice';
import toast from 'react-hot-toast';

class SignalRService {
  private connection: signalR.HubConnection | null = null;
  private reconnectTimer: NodeJS.Timeout | null = null;
  private reconnectAttempts = 0;
  private maxReconnectAttempts = 5;

  async connect(token: string): Promise<void> {
    if (this.connection?.state === signalR.HubConnectionState.Connected) {
      return;
    }

    // Default to relative URL to avoid mixed-content and ease deployments behind proxies
    const hubUrl = import.meta.env.VITE_SIGNALR_HUB_URL || '/chatHub';

    this.connection = new signalR.HubConnectionBuilder()
      .withUrl(hubUrl, {
        accessTokenFactory: () => token,
        // Prefer WebSockets, allow fallback transports for environments that block WS
        transport: signalR.HttpTransportType.WebSockets | signalR.HttpTransportType.ServerSentEvents,
      })
      .withAutomaticReconnect({
        nextRetryDelayInMilliseconds: (retryContext) => {
          if (retryContext.previousRetryCount === 0) return 0;
          if (retryContext.previousRetryCount === 1) return 2000;
          if (retryContext.previousRetryCount === 2) return 5000;
          if (retryContext.previousRetryCount === 3) return 10000;
          return 30000;
        }
      })
      .configureLogging(signalR.LogLevel.Information)
      .build();

    // Connection state handlers
    this.connection.onreconnecting(() => {
      store.dispatch(updateConnectionStatus('reconnecting'));
      console.log('SignalR: Reconnecting...');
    });

    this.connection.onreconnected(() => {
      store.dispatch(updateConnectionStatus('connected'));
      this.reconnectAttempts = 0;
      toast.success('Reconnected to server');
      console.log('SignalR: Reconnected');
    });

    this.connection.onclose(() => {
      store.dispatch(updateConnectionStatus('disconnected'));
      this.handleDisconnect();
    });

    // Register all hub methods
    this.registerHubMethods();

    try {
      await this.connection.start();
      store.dispatch(updateConnectionStatus('connected'));
      console.log('SignalR: Connected');
      
      // Join user's personal group
      const userId = localStorage.getItem('userId');
      if (userId) {
        await this.connection.invoke('JoinUserGroup', userId);
      }
    } catch (error) {
      console.error('SignalR connection failed:', error);
      store.dispatch(updateConnectionStatus('disconnected'));
      this.scheduleReconnect();
    }
  }

  private registerHubMethods(): void {
    if (!this.connection) return;

    // Receive message
    this.connection.on('ReceiveMessage', (message: any) => {
      store.dispatch(addMessage({
        id: message.id || Date.now().toString(),
        content: message.content,
        sender: message.sender || 'assistant',
        timestamp: new Date(message.timestamp || Date.now()),
        conversationId: message.conversationId,
        metadata: message.metadata
      }));
    });

    // Streaming message updates
    this.connection.on('StreamMessage', (chunk: string) => {
      store.dispatch(updateStreamingMessage(chunk));
    });

    // Typing indicator
    this.connection.on('UserTyping', (userId: string, isTyping: boolean) => {
      store.dispatch(setTyping(isTyping));
    });

    // Burning fire alert
    this.connection.on('BurningFireAlert', (data: {
      conversationId: string;
      issue: string;
      userId: string;
      timestamp: string;
    }) => {
      toast.error(`🔥 Burning Fire: ${data.issue}`, {
        duration: 5000,
        position: 'top-center'
      });
      console.log('Burning Fire Alert:', data);
    });

    // New thread created
    this.connection.on('NewThreadCreated', (data: {
      conversationId: string;
      conversationType: string;
      title: string;
    }) => {
      if (data.conversationType === 'burning_fire') {
        toast('🔥 New burning fire thread started', {
          icon: '🚨',
          duration: 3000
        });
      } else if (data.conversationType === 'tinker_level') {
        toast('🔧 New tinker level thread started', {
          icon: '🛠️',
          duration: 3000
        });
      }
    });

    // Session ended
    this.connection.on('SessionEnded', (data: {
      conversationId: string;
      sessionType: string;
      endTime: string;
    }) => {
      toast.success('Session ended. Great work today! 💪', {
        duration: 4000
      });
      console.log('Session ended:', data);
    });

    // Daily prompt completed
    this.connection.on('DailyPromptComplete', (data: {
      dayNumber: number;
      completedAt: string;
    }) => {
      store.dispatch(setDailyPromptComplete({
        dayNumber: data.dayNumber,
        completedAt: data.completedAt
      }));
      toast.success(`Day ${data.dayNumber} prompt completed! 🎉`, {
        duration: 4000
      });
    });

    // Error handling
    this.connection.on('Error', (error: string) => {
      toast.error(`Connection error: ${error}`);
      console.error('SignalR error:', error);
    });

    // Conversation updated
    this.connection.on('ConversationUpdated', (data: {
      conversationId: string;
      title?: string;
      metadata?: any;
    }) => {
      console.log('Conversation updated:', data);
      // Update conversation in store if needed
    });

    // Knowledge base result
    this.connection.on('KnowledgeBaseResult', (data: {
      conversationId: string;
      results: any[];
      query: string;
    }) => {
      console.log('Knowledge base results received:', data);
    });

    // System notification
    this.connection.on('SystemNotification', (message: string) => {
      toast(message, {
        icon: 'ℹ️',
        duration: 3000
      });
    });
  }

  private handleDisconnect(): void {
    console.log('SignalR: Disconnected');
    if (this.reconnectAttempts < this.maxReconnectAttempts) {
      this.scheduleReconnect();
    } else {
      toast.error('Connection lost. Please refresh the page.');
    }
  }

  private scheduleReconnect(): void {
    if (this.reconnectTimer) {
      clearTimeout(this.reconnectTimer);
    }

    const delay = Math.min(1000 * Math.pow(2, this.reconnectAttempts), 30000);
    this.reconnectAttempts++;

    this.reconnectTimer = setTimeout(async () => {
      console.log(`SignalR: Reconnect attempt ${this.reconnectAttempts}`);
      const token = localStorage.getItem('token');
      if (token) {
        await this.connect(token);
      }
    }, delay);
  }

  async disconnect(): Promise<void> {
    if (this.reconnectTimer) {
      clearTimeout(this.reconnectTimer);
      this.reconnectTimer = null;
    }

    if (this.connection) {
      try {
        await this.connection.stop();
      } catch (error) {
        console.error('Error stopping SignalR connection:', error);
      }
      this.connection = null;
    }

    this.reconnectAttempts = 0;
    store.dispatch(updateConnectionStatus('disconnected'));
  }

  // Send methods
  async sendMessage(message: string, conversationId?: string): Promise<void> {
    if (this.connection?.state !== signalR.HubConnectionState.Connected) {
      throw new Error('Not connected to server');
    }

    await this.connection.invoke('SendMessage', message, conversationId);
  }

  async sendTypingIndicator(isTyping: boolean, conversationId?: string): Promise<void> {
    if (this.connection?.state !== signalR.HubConnectionState.Connected) {
      return;
    }

    try {
      await this.connection.invoke('SendTypingIndicator', isTyping, conversationId);
    } catch (error) {
      console.error('Failed to send typing indicator:', error);
    }
  }

  async joinConversation(conversationId: string): Promise<void> {
    if (this.connection?.state !== signalR.HubConnectionState.Connected) {
      throw new Error('Not connected to server');
    }

    await this.connection.invoke('JoinConversation', conversationId);
  }

  async leaveConversation(conversationId: string): Promise<void> {
    if (this.connection?.state !== signalR.HubConnectionState.Connected) {
      return;
    }

    try {
      await this.connection.invoke('LeaveConversation', conversationId);
    } catch (error) {
      console.error('Failed to leave conversation:', error);
    }
  }

  async notifyBurningFire(issue: string, conversationId: string): Promise<void> {
    if (this.connection?.state !== signalR.HubConnectionState.Connected) {
      throw new Error('Not connected to server');
    }

    await this.connection.invoke('NotifyBurningFire', issue, conversationId);
  }

  async notifySessionEnd(conversationId: string, sessionType: string): Promise<void> {
    if (this.connection?.state !== signalR.HubConnectionState.Connected) {
      return;
    }

    try {
      await this.connection.invoke('NotifySessionEnd', conversationId, sessionType);
    } catch (error) {
      console.error('Failed to notify session end:', error);
    }
  }

  getConnectionState(): signalR.HubConnectionState | null {
    return this.connection?.state || null;
  }

  isConnected(): boolean {
    return this.connection?.state === signalR.HubConnectionState.Connected;
  }
}

export const signalRService = new SignalRService();