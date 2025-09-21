import React from 'react';
import ReactDOM from 'react-dom/client';
import { Provider } from 'react-redux';
import { Toaster } from 'react-hot-toast';
import App from './App';
import { store } from './store';
import './index.css';
import './styles/login.css';

// Register Service Worker for PWA
if ('serviceWorker' in navigator) {
  window.addEventListener('load', () => {
    navigator.serviceWorker.register('/sw.js')
      .then((registration) => {
        console.log('ServiceWorker registered:', registration);

        // Check for updates every hour
        setInterval(() => {
          registration.update();
        }, 3600000);

        // Handle updates
        registration.addEventListener('updatefound', () => {
          const newWorker = registration.installing;
          if (newWorker) {
            newWorker.addEventListener('statechange', () => {
              if (newWorker.state === 'activated') {
                // New service worker activated, show update prompt
                if (confirm('New version available! Reload to update?')) {
                  window.location.reload();
                }
              }
            });
          }
        });
      })
      .catch((error) => {
        console.error('ServiceWorker registration failed:', error);
      });
  });
}

// Request notification permission
if ('Notification' in window && Notification.permission === 'default') {
  Notification.requestPermission();
}

// Enable background sync
try {
  if ('serviceWorker' in navigator) {
    navigator.serviceWorker.ready.then((registration) => {
      (registration as any)?.sync?.register?.('sync-messages');
    }).catch(() => {});
  }
} catch {}

// Performance monitoring
if ('performance' in window) {
  window.addEventListener('load', () => {
    const perfData = window.performance.timing;
    const pageLoadTime = perfData.loadEventEnd - perfData.navigationStart;
    console.log(`Page load time: ${pageLoadTime}ms`);

    // Send performance metrics to analytics
    if (pageLoadTime > 3000) {
      console.warn('Page load time exceeds 3 seconds');
    }
  });
}

// Error boundary for production
window.addEventListener('error', (event) => {
  console.error('Global error:', event.error);
  // Send to error tracking service
});

window.addEventListener('unhandledrejection', (event) => {
  console.error('Unhandled promise rejection:', event.reason);
  // Send to error tracking service
});

// Prevent zoom on iOS
document.addEventListener('gesturestart', (e) => {
  e.preventDefault();
});

// Lock orientation on mobile (optional)
try {
  if ((screen as any)?.orientation?.lock) {
    (screen as any).orientation.lock('portrait').catch(() => {});
  }
} catch {}

// Initialize React app
const root = ReactDOM.createRoot(
  document.getElementById('root') as HTMLElement
);

root.render(
  <React.StrictMode>
    <Provider store={store}>
      <App />
      <Toaster
        position="top-right"
        toastOptions={{
          duration: 4000,
          style: {
            background: '#333',
            color: '#fff',
            borderRadius: '8px',
            padding: '16px',
          },
          success: {
            iconTheme: {
              primary: '#FFD700',
              secondary: '#000',
            },
          },
          error: {
            iconTheme: {
              primary: '#ff4444',
              secondary: '#fff',
            },
          },
        }}
      />
    </Provider>
  </React.StrictMode>
);

// Hot Module Replacement for development
if ((import.meta as any)?.hot) {
  (import.meta as any).hot.accept();
}

// Detect if app was launched from home screen
if (window.matchMedia('(display-mode: standalone)').matches) {
  console.log('App launched from home screen');
  // Track PWA usage
}

// Handle online/offline events
window.addEventListener('online', () => {
  console.log('Back online');
  // Trigger sync
  try {
    if ('serviceWorker' in navigator) {
      navigator.serviceWorker.ready.then((registration) => {
        (registration as any)?.sync?.register?.('sync-messages');
      }).catch(() => {});
    }
  } catch {}
});

window.addEventListener('offline', () => {
  console.log('Gone offline');
  // Show offline indicator
});

// Cleanup on app close
window.addEventListener('beforeunload', () => {
  // Save any pending data
  const state = store.getState();
  if (state.chat.messages.length > 0) {
    localStorage.setItem('pending_messages', JSON.stringify(state.chat.messages));
  }
});