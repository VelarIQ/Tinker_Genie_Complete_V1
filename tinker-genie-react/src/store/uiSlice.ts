import { createSlice, PayloadAction } from '@reduxjs/toolkit';

interface UIState {
  isSidebarOpen: boolean;
  isMobileMenuOpen: boolean;
  theme: 'light' | 'dark';
  showCommandPalette: boolean;
  showSettingsModal: boolean;
  showNewChatModal: boolean;
  connectionStatus: 'connected' | 'disconnected' | 'connecting';
}

const initialState: UIState = {
  isSidebarOpen: true,
  isMobileMenuOpen: false,
  theme: 'light',
  showCommandPalette: false,
  showSettingsModal: false,
  showNewChatModal: false,
  connectionStatus: 'disconnected',
};

const uiSlice = createSlice({
  name: 'ui',
  initialState,
  reducers: {
    toggleSidebar: (state) => {
      state.isSidebarOpen = !state.isSidebarOpen;
    },
    setSidebarOpen: (state, action: PayloadAction<boolean>) => {
      state.isSidebarOpen = action.payload;
    },
    toggleMobileMenu: (state) => {
      state.isMobileMenuOpen = !state.isMobileMenuOpen;
    },
    setMobileMenuOpen: (state, action: PayloadAction<boolean>) => {
      state.isMobileMenuOpen = action.payload;
    },
    setTheme: (state, action: PayloadAction<'light' | 'dark'>) => {
      state.theme = action.payload;
    },
    toggleTheme: (state) => {
      state.theme = state.theme === 'light' ? 'dark' : 'light';
    },
    setShowCommandPalette: (state, action: PayloadAction<boolean>) => {
      state.showCommandPalette = action.payload;
    },
    setShowSettingsModal: (state, action: PayloadAction<boolean>) => {
      state.showSettingsModal = action.payload;
    },
    setShowNewChatModal: (state, action: PayloadAction<boolean>) => {
      state.showNewChatModal = action.payload;
    },
    updateConnectionStatus: (state, action: PayloadAction<'connected' | 'disconnected' | 'connecting'>) => {
      state.connectionStatus = action.payload;
    },
  },
});

export const {
  toggleSidebar,
  setSidebarOpen,
  toggleMobileMenu,
  setMobileMenuOpen,
  setTheme,
  toggleTheme,
  setShowCommandPalette,
  setShowSettingsModal,
  setShowNewChatModal,
  updateConnectionStatus,
} = uiSlice.actions;

export default uiSlice.reducer;
