const API_URL = 'https://tinker.twobrain.ai';
let authToken = localStorage.getItem('token');
let userId = localStorage.getItem('userId');
let username = localStorage.getItem('username');

// Initialize Google Sign-In
window.onload = function() {
    if (authToken) {
        showApp();
    }
};

// Handle Google Sign-In - Fixed version
function handleGoogleSignIn(response) {
    try {
        // Parse the credential properly
        const credential = response.credential || response;
        
        // Send to our API for validation
        fetch(API_URL + '/api/auth/google', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ credential: credential })
        })
        .then(res => res.json())
        .then(data => {
            if (data.success) {
                authToken = data.token;
                userId = data.userId;
                username = data.name || 'User';
                
                localStorage.setItem('token', authToken);
                localStorage.setItem('userId', userId);
                localStorage.setItem('username', username);
                
                showApp();
            } else {
                document.getElementById('loginError').textContent = data.message || 'Sign-in failed';
            }
        })
        .catch(err => {
            console.error('API error:', err);
            document.getElementById('loginError').textContent = 'Connection error';
        });
    } catch (error) {
        console.error('Sign-in error:', error);
        document.getElementById('loginError').textContent = 'Sign-in failed';
    }
}

// Google Sign-In button click
document.addEventListener('DOMContentLoaded', function() {
    const googleBtn = document.getElementById('googleSignIn');
    if (googleBtn) {
        googleBtn.addEventListener('click', function() {
            google.accounts.id.initialize({
                client_id: '718408483036-cq6n4dqvgfqshf6mj51iomnru1gvfldo.apps.googleusercontent.com',
                callback: handleGoogleSignIn,
                auto_select: false,
                cancel_on_tap_outside: true
            });
            google.accounts.id.prompt();
        });
    }
    
    // Regular login
    document.getElementById('password')?.addEventListener('keypress', function(e) {
        if (e.key === 'Enter') login();
    });
    
    document.getElementById('username')?.addEventListener('keypress', function(e) {
        if (e.key === 'Enter') document.getElementById('password').focus();
    });
});

async function login() {
    const email = document.getElementById('username').value;
    const password = document.getElementById('password').value;
    const errorDiv = document.getElementById('loginError');
    
    if (!email || !password) {
        errorDiv.textContent = 'Please enter email and password';
        return;
    }
    
    errorDiv.textContent = '';
    
    try {
        const response = await fetch(API_URL + '/api/auth/login', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ username: email, password: password })
        });
        
        const data = await response.json();
        
        if (data.success || data.token) {
            authToken = data.token;
            userId = data.userId;
            username = email.split('@')[0];
            
            localStorage.setItem('token', authToken);
            localStorage.setItem('userId', userId);
            localStorage.setItem('username', username);
            
            showApp();
        } else {
            errorDiv.textContent = data.message || 'Invalid credentials';
        }
    } catch (error) {
        errorDiv.textContent = 'Connection error';
    }
}

function showApp() {
    document.getElementById('login').style.display = 'none';
    document.getElementById('app').style.display = 'block';
    document.getElementById('userName').textContent = username || 'Leader';
    getPrompt();
    getPreferences();
}

function getPrompt() {
    const promptDiv = document.getElementById('promptContent');
    const dayNum = parseInt(localStorage.getItem('currentDay') || '1');
    const prompts = [
        "Day 1: Call three past members today and ask why they left.",
        "Day 2: Review your pricing. Raise one service by 10% today.",
        "Day 3: Audit your coaches. Schedule a replacement for weakest."
    ];
    promptDiv.innerHTML = prompts[dayNum % prompts.length];
}

function getPreferences() {
    const prefsDiv = document.getElementById('prefsContent');
    const dayNum = parseInt(localStorage.getItem('currentDay') || '1');
    prefsDiv.innerHTML = 
        '<strong>Journey:</strong> Day ' + dayNum + ' of 180<br>' +
        '<strong>Completion:</strong> 85%<br>' +
        '<strong>Focus:</strong> Leadership<br>' +
        '<strong>Next:</strong> Tomorrow 9 AM';
}

function logout() {
    if (google?.accounts?.id) {
        google.accounts.id.disableAutoSelect();
    }
    localStorage.clear();
    location.reload();
}
