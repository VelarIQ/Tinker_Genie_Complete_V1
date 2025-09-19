// Aggressive No-Cache Service Worker for TinkerGenie PWA
// This service worker ensures NO caching and always fetches fresh content

const CACHE_VERSION = '1757388000'; // Updated timestamp

// Install event - clear all caches
self.addEventListener('install', function(event) {
  console.log('No-Cache Service Worker installing...');
  event.waitUntil(
    caches.keys().then(function(cacheNames) {
      return Promise.all(
        cacheNames.map(function(cacheName) {
          console.log('Deleting cache:', cacheName);
          return caches.delete(cacheName);
        })
      );
    }).then(function() {
      console.log('All caches cleared, Service Worker installed');
      return self.skipWaiting();
    })
  );
});

// Activate event - clear all caches again
self.addEventListener('activate', function(event) {
  console.log('No-Cache Service Worker activating...');
  event.waitUntil(
    caches.keys().then(function(cacheNames) {
      return Promise.all(
        cacheNames.map(function(cacheName) {
          console.log('Deleting cache:', cacheName);
          return caches.delete(cacheName);
        })
      );
    }).then(function() {
      console.log('All caches cleared, Service Worker activated');
      return self.clients.claim();
    })
  );
});

// Fetch event - ALWAYS go to network, never cache
self.addEventListener('fetch', function(event) {
  // Skip non-GET requests
  if (event.request.method !== 'GET') {
    return;
  }

  // Always fetch from network, never cache
  event.respondWith(
    fetch(event.request)
      .then(function(response) {
        // Don't cache anything - always return fresh content
        return response;
      })
      .catch(function(error) {
        console.log('Network fetch failed:', error);
        // If network fails, try to return a basic response
        if (event.request.url.includes('index.html')) {
          return new Response('<!DOCTYPE html><html><head><title>Loading...</title></head><body><h1>Loading TinkerGenie...</h1><script>window.location.reload(true);</script></body></html>', {
            headers: { 'Content-Type': 'text/html' }
          });
        }
        throw error;
      })
  );
});

// Message event - handle cache clearing
self.addEventListener('message', function(event) {
  if (event.data && event.data.type === 'CLEAR_CACHE') {
    console.log('Clearing all caches...');
    caches.keys().then(function(cacheNames) {
      return Promise.all(
        cacheNames.map(function(cacheName) {
          return caches.delete(cacheName);
        })
      );
    }).then(function() {
      console.log('All caches cleared');
      event.ports[0].postMessage({ success: true });
    });
  }
});
