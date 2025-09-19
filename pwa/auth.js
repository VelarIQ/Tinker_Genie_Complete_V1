// Authentication utilities for TinkerGenie PWA

function getToken() {
    return localStorage.getItem('token');
}

function setToken(token) {
    localStorage.setItem('token', token);
}

function clearAuth() {
    localStorage.removeItem('token');
    localStorage.removeItem('userId');
    localStorage.removeItem('userName');
    localStorage.removeItem('email');
}

async function authenticatedFetch(url, options = {}) {
    const token = getToken();
    if (!token || token === 'authenticated') {
        // Legacy token or missing - need to login
        window.location.href = '/pwa/login.html';
        throw new Error('Not authenticated');
    }

    const headers = new Headers(options.headers || {});
    headers.set('Authorization', 'Bearer ' + token);
    headers.set('Content-Type', 'application/json');
    
    const response = await fetch(url, { ...options, headers });
    
    // Handle 401 - try to refresh
    if (response.status === 401) {
        const refreshed = await refreshToken();
        if (refreshed) {
            // Retry with new token
            headers.set('Authorization', 'Bearer ' + getToken());
            return fetch(url, { ...options, headers });
        } else {
            // Refresh failed - redirect to login
            clearAuth();
            window.location.href = '/pwa/login.html';
            throw new Error('Authentication failed');
        }
    }
    
    return response;
}

async function refreshToken() {
    const token = getToken();
    if (!token || token === 'authenticated') return false;
    
    try {
        const response = await fetch('/api/auth/refresh', {
            method: 'POST',
            headers: {
                'Authorization': 'Bearer ' + token,
                'Content-Type': 'application/json'
            },
            body: JSON.stringify({ token })
        });
        
        if (response.ok) {
            const data = await response.json();
            if (data.success && data.accessToken) {
                setToken(data.accessToken);
                return true;
            }
        }
    } catch (error) {
        console.error('Token refresh failed:', error);
    }
    
    return false;
}

// Export for use in other scripts
window.authUtils = {
    getToken,
    setToken,
    clearAuth,
    authenticatedFetch,
    refreshToken
};
