import { createSlice, createAsyncThunk, PayloadAction } from '@reduxjs/toolkit';
import { chatService } from '../services/chatService';
import { conversationService, ConversationType } from '../services/conversationService';
import { knowledgeBaseService } from '../services/knowledgeBaseService';
import toast from 'react-hot-toast';

export interface Message {
  id: string;
  content: string;
  sender: 'user' | 'assistant';
  timestamp: Date;
  conversationId?: string;
  isStreaming?: boolean;
  metadata?: {
    sessionType?: string;
    isDailyPrompt?: boolean;
    dayNumber?: number;
    isNewThread?: boolean;
  };
}

export interface ChatState {
  messages: Message[];
  currentConversationId: string | null;
  currentConversationType: ConversationType;
  isLoading: boolean;
  isTyping: boolean;
  error: string | null;
  streamingMessage: string | null;
  dailyPrompt: {
    isComplete: boolean;
    dayNumber: number | null;
    promptTitle: string | null;
    completedAt?: string;
  };
  burningFireActive: boolean;
  tinkerLevelActive: boolean;
  sessionMetadata: {
    sessionType?: string;
    startedAt?: string;
    lastActivity?: string;
  };
}

const initialState: ChatState = {
  messages: [],
  currentConversationId: null,
  currentConversationType: ConversationType.GENERAL_CHAT,
  isLoading: false,
  isTyping: false,
  error: null,
  streamingMessage: null,
  dailyPrompt: {
    isComplete: false,
    dayNumber: null,
    promptTitle: null
  },
  burningFireActive: false,
  tinkerLevelActive: false,
  sessionMetadata: {}
};

// Async thunk for sending messages with three-tier logic
export const sendMessage = createAsyncThunk(
  'chat/sendMessage',
  async (message: string, { getState, dispatch }) => {
    const state = getState() as { chat: ChatState };

    // Set typing indicator for AI response
    dispatch(setTyping(true));

    // Check if we have a pending conversation type that needs title update
    const pendingType = (window as any).__pendingConversationType;
    if (pendingType && state.chat.currentConversationId) {
      // Update conversation title based on the actual issue/challenge described
      await conversationService.updateConversationTitleFromContent(
        state.chat.currentConversationId,
        message,
        pendingType,
        { dayNumber: state.chat.dailyPrompt.dayNumber }
      );
      delete (window as any).__pendingConversationType;
    }

    // Detect conversation type from message
    const detection = conversationService.detectConversationType(message);

    // Handle explicit conversation type changes (should not send message, just setup)
    if (detection.isExplicitChange) {
      // These are handled in ChatPage now to create new sessions
      return { response: '', conversationId: state.chat.currentConversationId } as const;
    }

    // Search knowledge base if it's a burning fire or tinker level
    if (state.chat.currentConversationType === ConversationType.BURNING_FIRE ||
        state.chat.currentConversationType === ConversationType.TINKER_LEVEL) {
      knowledgeBaseService.searchContent(message, state.chat.currentConversationType)
        .then(results => {
          console.log('Knowledge base results:', results);
        })
        .catch(err => {
          console.error('Knowledge base search failed:', err);
        });
    }

    try {
      // Send the message
      const response = await chatService.sendMessage(
        message,
        state.chat.currentConversationId || undefined
      );

      // Clear typing indicator when response is received
      dispatch(setTyping(false));

      return response;
    } catch (error) {
      // Clear typing indicator on error
      dispatch(setTyping(false));
      throw error;
    }
  }
);

// Start a new chat with specific type
export const startNewChat = createAsyncThunk(
  'chat/startNewChat',
  async (conversationType: ConversationType = ConversationType.GENERAL_CHAT, { dispatch }) => {
    // Create new conversation
    const metadata: any = {};

    if (conversationType === ConversationType.DAILY_PROMPT) {
      const status = await chatService.checkDailyPromptStatus();
      metadata.dayNumber = status.dayNumber;
    }

    const conversation = await conversationService.createConversation(
      conversationType,
      undefined,
      metadata
    );

    // Clear current messages
    dispatch(clearMessages());

    // Set new conversation
    dispatch(setCurrentConversation(conversation.id));
    dispatch(setConversationType(conversationType));

    // If it's a daily prompt, the session start will be handled by backend via first send
    return {
      conversationId: conversation.id,
      conversationType
    } as const;
  }
);

// Load conversation with messages
export const loadConversation = createAsyncThunk(
  'chat/loadConversation',
  async (conversationId: string, { dispatch }) => {
    const conversation = await conversationService.getConversation(conversationId);
    // Fetch messages via REST endpoint
    const res = await fetch(`/api/conversations/${conversationId}/messages`, {
      headers: { 'Authorization': `Bearer ${localStorage.getItem('tinker_genie_token') || ''}` }
    });
    const data = await res.json();
    const messages = (data?.messages || []).map((m: any) => ({
      id: m.id,
      content: m.content,
      sender: m.role === 'assistant' ? 'assistant' : 'user',
      timestamp: new Date(m.timestamp),
      conversationId
    })) as Message[];

    // Set conversation type from metadata
    const type = conversation.metadata?.conversationType || ConversationType.GENERAL_CHAT;
    dispatch(setConversationType(type as ConversationType));

    return {
      conversation,
      messages
    } as const;
  }
);

// Check and start daily prompt if needed
export const checkDailyPrompt = createAsyncThunk(
  'chat/checkDailyPrompt',
  async (_, { dispatch }) => {
    const status = await chatService.checkDailyPromptStatus();

    if (!status.isComplete) {
      // Start daily prompt conversation
      const result = await dispatch(startNewChat(ConversationType.DAILY_PROMPT));
      return {
        shouldStart: true,
        ...status,
        result
      } as const;
    }

    return {
      shouldStart: false,
      ...status
    } as const;
  }
);

const chatSlice = createSlice({
  name: 'chat',
  initialState,
  reducers: {
    addMessage: (state, action: PayloadAction<Message>) => {
      state.messages.push(action.payload);
    },
    updateStreamingMessage: (state, action: PayloadAction<string>) => {
      state.streamingMessage = action.payload;
    },
    setTyping: (state, action: PayloadAction<boolean>) => {
      state.isTyping = action.payload;
    },
    clearMessages: (state) => {
      state.messages = [];
      state.streamingMessage = null;
    },
    setCurrentConversation: (state, action: PayloadAction<string>) => {
      state.currentConversationId = action.payload;
    },
    setConversationType: (state, action: PayloadAction<ConversationType>) => {
      state.currentConversationType = action.payload;

      // Update active flags
      state.burningFireActive = action.payload === ConversationType.BURNING_FIRE;
      state.tinkerLevelActive = action.payload === ConversationType.TINKER_LEVEL;
    },
    setDailyPromptComplete: (state, action: PayloadAction<{ 
      dayNumber: number; 
      promptTitle?: string;
      completedAt?: string;
    }>) => {
      state.dailyPrompt = {
        isComplete: true,
        dayNumber: action.payload.dayNumber,
        promptTitle: action.payload.promptTitle || null,
        completedAt: action.payload.completedAt
      };
    },
    updateSessionMetadata: (state, action: PayloadAction<Partial<ChatState['sessionMetadata']>>) => {
      state.sessionMetadata = {
        ...state.sessionMetadata,
        ...action.payload,
        lastActivity: new Date().toISOString()
      };
    }
  },
  extraReducers: (builder) => {
    builder
      // Send message
      .addCase(sendMessage.pending, (state, action) => {
        state.isLoading = true;
        state.error = null;
        // Optimistically add the user's message
        const userText = (action as any)?.meta?.arg as string;
        if (typeof userText === 'string' && userText.trim().length > 0) {
          state.messages.push({
            id: `${Date.now()}-user`,
            content: userText,
            sender: 'user',
            timestamp: new Date(),
            conversationId: state.currentConversationId || undefined,
          });
        }
      })
      .addCase(sendMessage.fulfilled, (state, action) => {
        state.isLoading = false;
        state.isTyping = false; // Clear typing when response received

        // Update conversation ID if provided
        const payload: any = action.payload as any;
        if (payload?.conversationId) {
          state.currentConversationId = payload.conversationId as string;
        }

        // Daily prompt completion flag if provided
        if (payload && typeof payload.isDailyPromptComplete === 'boolean' && payload.isDailyPromptComplete) {
          state.dailyPrompt.isComplete = true;
          state.dailyPrompt.completedAt = new Date().toISOString();
        }

        // Handle session type from payload
        if (payload?.sessionType) {
          state.sessionMetadata.sessionType = String(payload.sessionType);
        }

        // Append assistant response on REST fallback
        if (payload?.response && payload.response !== 'Message sent via SignalR') {
          state.messages.push({
            id: `${Date.now()}-assistant`,
            content: String(payload.response),
            sender: 'assistant',
            timestamp: new Date(),
            conversationId: state.currentConversationId || undefined,
          });
        }
      })
      .addCase(sendMessage.rejected, (state, action) => {
        state.isLoading = false;
        state.isTyping = false; // Clear typing on error
        state.error = action.error.message || 'Failed to send message';
        toast.error('Failed to send message');
      })

      // Start new chat
      .addCase(startNewChat.fulfilled, (state, action) => {
        state.currentConversationId = action.payload.conversationId;
        state.currentConversationType = action.payload.conversationType;
        state.messages = [];
      })

      // Load conversation
      .addCase(loadConversation.fulfilled, (state, action) => {
        state.currentConversationId = action.payload.conversation.id;
        state.messages = action.payload.messages;
      })

      // Check daily prompt
      .addCase(checkDailyPrompt.fulfilled, (state, action) => {
        state.dailyPrompt = {
          isComplete: (action.payload as any).isComplete,
          dayNumber: (action.payload as any).dayNumber,
          promptTitle: null,
          completedAt: (action.payload as any).completedAt
        };
      });
  }
});

export const {
  addMessage,
  updateStreamingMessage,
  setTyping,
  clearMessages,
  setCurrentConversation,
  setConversationType,
  setDailyPromptComplete,
  updateSessionMetadata
} = chatSlice.actions;

export default chatSlice.reducer;