import { createSlice, createAsyncThunk, PayloadAction } from '@reduxjs/toolkit';
import { conversationService } from '../services/conversationService';

export interface Conversation {
  id: string;
  title: string;
  lastMessage: string;
  lastMessageTime: string;
  userId: string;
  type?: string;
  metadata?: Record<string, any>;
}

interface ConversationState {
  conversations: Conversation[];
  currentConversation: Conversation | null;
  isLoading: boolean;
  error: string | null;
  hasMore: boolean;
  page: number;
}

const initialState: ConversationState = {
  conversations: [],
  currentConversation: null,
  isLoading: false,
  error: null,
  hasMore: true,
  page: 1,
};

// Async thunks
export const fetchConversations = createAsyncThunk(
  'conversation/fetchConversations',
  async ({ page = 1, limit = 20 }: { page?: number; limit?: number }) => {
    const response = await conversationService.getConversations(page, limit);
    return response;
  }
);

export const fetchRecentConversations = createAsyncThunk(
  'conversation/fetchRecentConversations',
  async () => {
    const response = await conversationService.getRecentConversations();
    return response;
  }
);

export const deleteConversation = createAsyncThunk(
  'conversation/deleteConversation',
  async (conversationId: string) => {
    await conversationService.deleteConversation(conversationId);
    return conversationId;
  }
);

export const updateConversationTitle = createAsyncThunk(
  'conversation/updateTitle',
  async ({ id, title }: { id: string; title: string }) => {
    const response = await conversationService.updateConversationTitle(id, title);
    return response;
  }
);

const conversationSlice = createSlice({
  name: 'conversation',
  initialState,
  reducers: {
    setCurrentConversation: (state, action: PayloadAction<Conversation | null>) => {
      state.currentConversation = action.payload;
    },
    addConversation: (state, action: PayloadAction<Conversation>) => {
      const exists = state.conversations.find((c) => c.id === action.payload.id);
      if (!exists) {
        state.conversations.unshift(action.payload);
      }
    },
    updateConversation: (state, action: PayloadAction<Partial<Conversation> & { id: string }>) => {
      const index = state.conversations.findIndex((c) => c.id === action.payload.id);
      if (index !== -1) {
        state.conversations[index] = { ...state.conversations[index], ...action.payload };
      }
    },
    clearConversations: (state) => {
      state.conversations = [];
      state.currentConversation = null;
      state.page = 1;
      state.hasMore = true;
    },
  },
  extraReducers: (builder) => {
    // Fetch Conversations
    builder
      .addCase(fetchConversations.pending, (state) => {
        state.isLoading = true;
        state.error = null;
      })
      .addCase(fetchConversations.fulfilled, (state, action) => {
        state.isLoading = false;
        if (action.meta.arg.page === 1) {
          state.conversations = action.payload.conversations;
        } else {
          state.conversations = [...state.conversations, ...action.payload.conversations];
        }
        state.hasMore = action.payload.hasMore;
        state.page = action.payload.page;
      })
      .addCase(fetchConversations.rejected, (state, action) => {
        state.isLoading = false;
        state.error = action.error.message || 'Failed to fetch conversations';
      });

    // Fetch Recent Conversations
    builder
      .addCase(fetchRecentConversations.fulfilled, (state, action) => {
        state.conversations = action.payload;
      });

    // Delete Conversation
    builder
      .addCase(deleteConversation.fulfilled, (state, action) => {
        state.conversations = state.conversations.filter((c) => c.id !== action.payload);
        if (state.currentConversation?.id === action.payload) {
          state.currentConversation = null;
        }
      });

    // Update Title
    builder
      .addCase(updateConversationTitle.fulfilled, (state, action) => {
        const index = state.conversations.findIndex((c) => c.id === action.payload.id);
        if (index !== -1) {
          state.conversations[index].title = action.payload.title;
        }
        if (state.currentConversation?.id === action.payload.id) {
          state.currentConversation.title = action.payload.title;
        }
      });
  },
});

export const {
  setCurrentConversation,
  addConversation,
  updateConversation,
  clearConversations,
} = conversationSlice.actions;

export default conversationSlice.reducer;
