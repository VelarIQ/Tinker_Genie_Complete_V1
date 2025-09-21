import { useEffect, useState } from 'react';
import { BrowserRouter as Router, Routes, Route, Navigate } from 'react-router-dom';
import { Provider } from 'react-redux';
import { Toaster } from 'react-hot-toast';
import { store } from './store';
import { useAppDispatch, useAppSelector } from './hooks/redux';
import { checkAuth } from './store/authSlice';
import { preferencesService } from './services/preferencesService';
import LoginPage from './pages/LoginPage';
import ChatPage from './pages/ChatPage';
import DashboardPage from './pages/DashboardPage';
import SettingsPage from './pages/SettingsPage';
import PreferencesModal from './components/chat/PreferencesModal';
import LoadingSpinner from './components/common/LoadingSpinner';

function AppContent() {
  const dispatch = useAppDispatch();
  const { isAuthenticated, isLoading } = useAppSelector((state) => state.auth);
  const [showPreferencesModal, setShowPreferencesModal] = useState(false);
  const [isFirstTimeUser, setIsFirstTimeUser] = useState(false);

  useEffect(() => {
    // Validate token on app load
    dispatch(checkAuth());
  }, [dispatch]);

  useEffect(() => {
    // Check preferences when authenticated
    if (isAuthenticated) {
      checkUserPreferences();
    }
  }, [isAuthenticated]);

  const checkUserPreferences = async () => {
    try {
      const prefs = await preferencesService.getPreferences();
      // Only show modal if onboarding not completed AND no communication style set
      if (!prefs.onboardingCompleted && (!prefs.communicationStyle || !prefs.notificationTime)) {
        setIsFirstTimeUser(true);
        setShowPreferencesModal(true);
      }
    } catch (error) {
      // If error and no cached preferences, show modal
      if (!preferencesService.hasPreferencesSet()) {
        setIsFirstTimeUser(true);
        setShowPreferencesModal(true);
      }
    }
  };

  if (isLoading) {
    return (
      <div className="h-screen flex items-center justify-center">
        <LoadingSpinner size="large" />
      </div>
    );
  }

  return (
    <>
      <Router>
        <Routes>
          <Route
            path="/login"
            element={
              isAuthenticated ? <Navigate to="/chat" replace /> : <LoginPage />
            }
          />
          <Route
            path="/chat"
            element={
              isAuthenticated ? <ChatPage /> : <Navigate to="/login" replace />
            }
          />
          <Route
            path="/dashboard"
            element={
              isAuthenticated ? <DashboardPage /> : <Navigate to="/login" replace />
            }
          />
          <Route
            path="/settings"
            element={
              isAuthenticated ? <SettingsPage /> : <Navigate to="/login" replace />
            }
          />
          <Route
            path="/"
            element={<Navigate to={isAuthenticated ? "/chat" : "/login"} replace />}
          />
        </Routes>
      </Router>

      <Toaster
        position="top-center"
        toastOptions={{
          duration: 4000,
          style: {
            background: '#363636',
            color: '#fff',
          },
          success: {
            style: {
              background: '#10b981',
            },
          },
          error: {
            style: {
              background: '#ef4444',
            },
          },
        }}
      />

      {/* Preferences Modal - Only shows once for new users */}
      <PreferencesModal 
        isOpen={showPreferencesModal}
        onClose={() => setShowPreferencesModal(false)}
        isFirstTime={isFirstTimeUser}
      />
    </>
  );
}

function App() {
  return (
    <Provider store={store}>
      <AppContent />
    </Provider>
  );
}

export default App;