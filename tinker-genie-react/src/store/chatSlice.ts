import { createSlice, createAsyncThunk, PayloadAction } from '@reduxjs/toolkit';
import { chatService, ChatResponse } from '../services/chatService';
import { conversationService, ConversationType } from '../services/conversationService';
import { knowledgeBaseService } from '../services/knowledgeBaseService';
import { signalRService } from '../services/signalRService';
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
    
    // Detect conversation type
    const detection = conversationService.detectConversationType(message);
    
    // Handle conversation type changes
    if (detection.isExplicitChange) {
      // Update conversation type
      dispatch(setConversationType(detection.type));
      
      // Create new conversation for burning fires and tinker level
      if (detection.type === ConversationType.BURNING_FIRE || 
          detection.type === ConversationType.TINKER_LEVEL) {
        
        // Create new conversation with initial title
        const conversation = await conversationService.createConversation(
          detection.type,
          undefined,
          { triggeredPhrase: detection.triggeredPhrase }
        );
        
        dispatch(setCurrentConversation(conversation.id));
        
        // Update title after first real message
        const updateTitle = async (firstMessage: string) => {
          await conversationService.updateConversationTitleFromContent(
            conversation.id,
            firstMessage,
            detection.type,
            { dayNumber: state.chat.dailyPrompt.dayNumber }
          );
        };
        
        // Store title update function for next message
        (window as any).__pendingTitleUpdate = updateTitle;
      }
    }
    
    // If we have a pending title update, execute it
    if ((window as any).__pendingTitleUpdate && 
        !detection.isExplicitChange) {
      await (window as any).__pendingTitleUpdate(message);
      delete (window as any).__pendingTitleUpdate;
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
    
    // Send the message
    const response = await chatService.sendMessage(
      message,
      state.chat.currentConversationId || undefined,
      state.chat.currentConversationType
    );
    
    return response;
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
    
    // If it's a daily prompt, start the session
    if (conversationType === ConversationType.DAILY_PROMPT) {
      const sessionResponse = await chatService.startSession();
      return {
        conversationId: conversation.id,
        conversationType,
        sessionResponse
      };
    }
    
    return {
      conversationId: conversation.id,
      conversationType
    };
  }
);

// Load conversation with messages
export const loadConversation = createAsyncThunk(
  'chat/loadConversation',
  async (conversationId: string, { dispatch }) => {
    const conversation = await conversationService.getConversation(conversationId);
    const messages = await chatService.getConversation(conversationId);
    
    // Set conversation type from metadata
    const type = conversation.metadata?.conversationType || ConversationType.GENERAL_CHAT;
    dispatch(setConversationType(type as ConversationType));
    
    return {
      conversation,
      messages: messages.messages
    };
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
      };
    }
    
    return {
      shouldStart: false,
      ...status
    };
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
      .addCase(sendMessage.pending, (state) => {
        state.isLoading = true;
        state.error = null;
      })
      .addCase(sendMessage.fulfilled, (state, action) => {
        state.isLoading = false;
        
        // Update conversation ID if provided
        if (action.payload.conversationId) {
          state.currentConversationId = action.payload.conversationId;
        }
        
        // Update daily prompt status
        if (action.payload.isDailyPromptComplete) {
          state.dailyPrompt.isComplete = true;
          state.dailyPrompt.completedAt = new Date().toISOString();
        }
        
        // Handle session type changes
        if (action.payload.sessionType) {
          state.sessionMetadata.sessionType = action.payload.sessionType;
        }
      })
      .addCase(sendMessage.rejected, (state, action) => {
        state.isLoading = false;
        state.error = action.error.message || 'Failed to send message';
        toast.error('Failed to send message');
      })
      
      // Start new chat
      .addCase(startNewChat.fulfilled, (state, action) => {
        state.currentConversationId = action.payload.conversationId;
        state.currentConversationType = action.payload.conversationType;
        state.messages = [];
        
        // If daily prompt, add the prompt message
        if (action.payload.conversationType === ConversationType.DAILY_PROMPT && 
            action.payload.sessionResponse) {
          state.messages.push({
            id: Date.now().toString(),
            content: action.payload.sessionResponse.response,
            sender: 'assistant',
            timestamp: new Date(),
            conversationId: action.payload.conversationId,
            metadata: {
              isDailyPrompt: true,
              dayNumber: action.payload.sessionResponse.dayNumber
            }
          });
        }
      })
      
      // Load conversation
      .addCase(loadConversation.fulfilled, (state, action) => {
        state.currentConversationId = action.payload.conversation.id;
        state.messages = action.payload.messages;
      })
      
      // Check daily prompt
      .addCase(checkDailyPrompt.fulfilled, (state, action) => {
        state.dailyPrompt = {
          isComplete: action.payload.isComplete,
          dayNumber: action.payload.dayNumber,
          promptTitle: null,
          completedAt: action.payload.completedAt
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