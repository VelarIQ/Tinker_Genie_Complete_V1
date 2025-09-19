// TinkerGenie Service Worker v2.0
// Enhanced with intelligent caching and offline support

const CACHE_NAME = 'tinkergenie-v2.0';
const DYNAMIC_CACHE = 'tinkergenie-dynamic-v2.0';
const API_CACHE = 'tinkergenie-api-v2.0';

// Assets to cache immediately
const STATIC_ASSETS = [
  '/',
  '/index.html',
  '/manifest.json',
  '/icon-192.png',
  '/icon-512.png',
];

// Install event - cache static assets
self.addEventListener('install', (event) => {
  console.log('[SW] Installing service worker...');
  event.waitUntil(
    caches.open(CACHE_NAME).then((cache) => {
      console.log('[SW] Caching static assets');
      return cache.addAll(STATIC_ASSETS);
    })
  );
  self.skipWaiting();
});

// Activate event - clean up old caches
self.addEventListener('activate', (event) => {
  console.log('[SW] Activating service worker...');
  event.waitUntil(
    caches.keys().then((cacheNames) => {
      return Promise.all(
        cacheNames
          .filter((name) => name !== CACHE_NAME && name !== DYNAMIC_CACHE && name !== API_CACHE)
          .map((name) => {
            console.log('[SW] Deleting old cache:', name);
            return caches.delete(name);
          })
      );
    })
  );
  self.clients.claim();
});

// Fetch event - intelligent caching strategies
self.addEventListener('fetch', (event) => {
  const { request } = event;
  const url = new URL(request.url);

  // Skip chrome-extension and non-HTTP(S) requests
  if (url.protocol === 'chrome-extension:' || !url.protocol.startsWith('http')) {
    return;
  }

  // API calls - Network first, fallback to cache
  if (url.pathname.startsWith('/api/') || url.pathname.startsWith('/chatHub')) {
    event.respondWith(
      fetch(request)
        .then((response) => {
          // Clone the response before caching
          const responseToCache = response.clone();
          
          // Only cache successful responses
          if (response.status === 200) {
            caches.open(API_CACHE).then((cache) => {
              cache.put(request, responseToCache);
            });
          }
          
          return response;
        })
        .catch(() => {
          // If network fails, try cache
          return caches.match(request).then((response) => {
            if (response) {
              console.log('[SW] Serving API from cache:', request.url);
              return response;
            }
            
            // Return offline response for API calls
            return new Response(
              JSON.stringify({ 
                error: 'You are currently offline. Please check your connection.' 
              }),
              {
                status: 503,
                statusText: 'Service Unavailable',
                headers: new Headers({
                  'Content-Type': 'application/json'
                })
              }
            );
          });
        })
    );
    return;
  }

  // Static assets - Cache first, fallback to network
  if (request.destination === 'image' || 
      request.destination === 'font' ||
      url.pathname.match(/\.(css|js|woff2?)$/)) {
    event.respondWith(
      caches.match(request).then((response) => {
        if (response) {
          return response;
        }
        
        return fetch(request).then((response) => {
          // Cache successful responses
          if (response.status === 200) {
            const responseToCache = response.clone();
            caches.open(DYNAMIC_CACHE).then((cache) => {
              cache.put(request, responseToCache);
            });
          }
          return response;
        });
      })
    );
    return;
  }

  // HTML pages - Network first for freshness
  if (request.destination === 'document' || request.headers.get('accept')?.includes('text/html')) {
    event.respondWith(
      fetch(request)
        .then((response) => {
          const responseToCache = response.clone();
          caches.open(CACHE_NAME).then((cache) => {
            cache.put(request, responseToCache);
          });
          return response;
        })
        .catch(() => {
          return caches.match(request).then((response) => {
            if (response) {
              return response;
            }
            // Fallback to index.html for client-side routing
            return caches.match('/index.html');
          });
        })
    );
    return;
  }

  // Default - Try network, fallback to cache
  event.respondWith(
    fetch(request).catch(() => caches.match(request))
  );
});

// Background sync for offline messages
self.addEventListener('sync', (event) => {
  if (event.tag === 'sync-messages') {
    console.log('[SW] Syncing offline messages...');
    event.waitUntil(syncOfflineMessages());
  }
});

// Push notifications
self.addEventListener('push', (event) => {
  if (!event.data) return;

  const data = event.data.json();
  const options = {
    body: data.body || 'You have a new message',
    icon: '/icon-192.png',
    badge: '/icon-96.png',
    vibrate: [100, 50, 100],
    data: {
      dateOfArrival: Date.now(),
      primaryKey: 1
    },
    actions: [
      {
        action: 'view',
        title: 'View',
      },
      {
        action: 'close',
        title: 'Close',
      }
    ]
  };

  event.waitUntil(
    self.registration.showNotification('TinkerGenie', options)
  );
});

// Notification click handler
self.addEventListener('notificationclick', (event) => {
  event.notification.close();

  if (event.action === 'view') {
    event.waitUntil(
      clients.openWindow('/')
    );
  }
});

// Helper function to sync offline messages
async function syncOfflineMessages() {
  try {
    // Get offline messages from IndexedDB or localStorage
    const messages = await getOfflineMessages();
    
    if (messages && messages.length > 0) {
      // Send messages to server
      const response = await fetch('/api/chat/sync', {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
        },
        body: JSON.stringify({ messages })
      });

      if (response.ok) {
        // Clear offline messages after successful sync
        await clearOfflineMessages();
        console.log('[SW] Offline messages synced successfully');
      }
    }
  } catch (error) {
    console.error('[SW] Failed to sync offline messages:', error);
  }
}

// Placeholder functions for IndexedDB operations
async function getOfflineMessages() {
  // Implementation would retrieve messages from IndexedDB
  return [];
}

async function clearOfflineMessages() {
  // Implementation would clear messages from IndexedDB
  return true;
}

// Performance optimization - clean up old caches periodically
setInterval(() => {
  caches.open(API_CACHE).then((cache) => {
    cache.keys().then((keys) => {
      // Keep only last 50 API responses
      if (keys.length > 50) {
        keys.slice(0, keys.length - 50).forEach((key) => {
          cache.delete(key);
        });
      }
    });
  });
}, 60000); // Every minute

console.log('[SW] Service Worker loaded');

