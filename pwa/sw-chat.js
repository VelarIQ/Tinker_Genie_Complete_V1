// Chat-Optimized Service Worker for TinkerGenie PWA
// This service worker is designed specifically for real-time chat applications
// It caches static assets but bypasses caching for API calls

const CACHE_VERSION = '1757388000';
const STATIC_CACHE = `tinker-genie-static-${CACHE_VERSION}`;
const DYNAMIC_CACHE = `tinker-genie-dynamic-${CACHE_VERSION}`;

// Static assets to cache
const STATIC_ASSETS = [
    '/pwa/',
    '/pwa/index.html',
    '/pwa/manifest.json',
    '/pwa/icon-192.png',
    '/pwa/icon-512.png',
    '/pwa/favicon.ico'
];

// API endpoints that should never be cached
const API_ENDPOINTS = [
    '/api/chat',
    '/api/curriculum',
    '/api/preferences',
    '/api/auth'
];

// Install event - cache static assets only
self.addEventListener('install', event => {
    console.log('Chat Service Worker installing...');
    event.waitUntil(
        caches.open(STATIC_CACHE).then(cache => {
            console.log('Caching static assets...');
            return cache.addAll(STATIC_ASSETS);
        }).then(() => {
            console.log('Static assets cached successfully');
            return self.skipWaiting();
        })
    );
});

// Activate event - clean up old caches
self.addEventListener('activate', event => {
    console.log('Chat Service Worker activating...');
    event.waitUntil(
        caches.keys().then(cacheNames => {
            return Promise.all(
                cacheNames.map(cacheName => {
                    if (cacheName !== STATIC_CACHE && cacheName !== DYNAMIC_CACHE) {
                        console.log('Deleting old cache:', cacheName);
                        return caches.delete(cacheName);
                    }
                })
            );
        }).then(() => {
            console.log('Old caches cleaned up');
            return self.clients.claim();
        })
    );
});

// Fetch event - handle different types of requests
self.addEventListener('fetch', event => {
    const { request } = event;
    const url = new URL(request.url);

    // Skip service worker for API calls - always fetch fresh
    if (isAPIRequest(url)) {
        console.log('API request - bypassing cache:', url.pathname);
        event.respondWith(fetch(request));
        return;
    }

    // Handle static assets with cache-first strategy
    if (isStaticAsset(url)) {
        event.respondWith(
            caches.match(request).then(response => {
                if (response) {
                    console.log('Serving from cache:', url.pathname);
                    return response;
                }
                return fetch(request).then(fetchResponse => {
                    // Cache successful responses
                    if (fetchResponse.ok) {
                        const responseClone = fetchResponse.clone();
                        caches.open(STATIC_CACHE).then(cache => {
                            cache.put(request, responseClone);
                        });
                    }
                    return fetchResponse;
                });
            })
        );
        return;
    }

    // For all other requests, try network first
    event.respondWith(
        fetch(request).catch(() => {
            return caches.match(request);
        })
    );
});

// Helper function to check if request is an API call
function isAPIRequest(url) {
    return API_ENDPOINTS.some(endpoint => url.pathname.startsWith(endpoint));
}

// Helper function to check if request is a static asset
function isStaticAsset(url) {
    return url.pathname.startsWith('/pwa/') && 
           (url.pathname.endsWith('.html') || 
            url.pathname.endsWith('.css') || 
            url.pathname.endsWith('.js') || 
            url.pathname.endsWith('.png') || 
            url.pathname.endsWith('.ico') || 
            url.pathname.endsWith('.json'));
}

// Handle messages from the main thread
self.addEventListener('message', event => {
    if (event.data && event.data.type === 'SKIP_WAITING') {
        self.skipWaiting();
    }
});

console.log('Chat Service Worker loaded successfully');
