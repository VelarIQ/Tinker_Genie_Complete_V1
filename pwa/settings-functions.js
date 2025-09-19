// Settings functionality for TinkerGenie

let currentSettings = {
    communicationStyle: 'concise',
    dailyPromptTime: '09:00',
    timeZone: Intl.DateTimeFormat().resolvedOptions().timeZone,
    theme: 'dark'
};

// Load user preferences on page load
async function loadSettings() {
    const userId = localStorage.getItem('userId');
    const authToken = localStorage.getItem('authToken');
    
    if (!userId || !authToken) {
        console.error('Not authenticated');
        return;
    }
    
    try {
        const response = await fetch(`/api/preferences/${userId}`, {
            headers: {
                'Authorization': `Bearer ${authToken}`
            }
        });
        
        if (response.ok) {
            const data = await response.json();
            currentSettings = {
                communicationStyle: data.communicationStyle || 'concise',
                dailyPromptTime: data.dailyPromptTime || '09:00',
                timeZone: data.timeZone || currentSettings.timeZone,
                theme: localStorage.getItem('theme') || 'dark'
            };
            
            // Update UI
            updateSettingsUI();
            
            // Store scheduled time for welcome screen
            localStorage.setItem('scheduledTime', currentSettings.dailyPromptTime);
        }
    } catch (error) {
        console.error('Failed to load preferences:', error);
    }
}

// Update the settings UI with current values
function updateSettingsUI() {
    // Update Response Style
    const styleElement = document.querySelector('.setting-group:nth-child(1) .setting-value-text');
    if (styleElement) {
        const styleMap = {
            'concise': 'Concise',
            'balanced': 'Balanced',
            'detailed': 'Detailed'
        };
        styleElement.textContent = styleMap[currentSettings.communicationStyle] || 'Concise';
    }
    
    // Update Daily Prompt Time
    const timeElement = document.querySelector('.setting-group:nth-child(2) .setting-value-text');
    if (timeElement) {
        // Convert 24h to 12h format
        const [hours, minutes] = currentSettings.dailyPromptTime.split(':');
        const h = parseInt(hours);
        const ampm = h >= 12 ? 'PM' : 'AM';
        const displayHours = h > 12 ? h - 12 : (h === 0 ? 12 : h);
        timeElement.textContent = `${displayHours}:${minutes} ${ampm}`;
    }
    
    // Update Time Zone
    const tzElement = document.querySelector('.setting-group:nth-child(3) .setting-value-text');
    if (tzElement) {
        tzElement.textContent = currentSettings.timeZone;
    }
    
    // Update Current Progress
    const progressTextElement = document.getElementById('progress-text');
    const progressBarElement = document.getElementById('progress-bar');
    if (progressTextElement) {
        const currentDay = parseInt(localStorage.getItem(`currentDay_${localStorage.getItem('userId')}`) || '1');
        progressTextElement.textContent = `Day ${currentDay} of 180`;
        
        // Update progress bar width (percentage based on 180 days)
        if (progressBarElement) {
            const progressPercentage = (currentDay / 180) * 100;
            progressBarElement.style.width = `${progressPercentage}%`;
        }
    }
}

// Save settings to backend
async function saveSettings() {
    const userId = localStorage.getItem('userId');
    const authToken = localStorage.getItem('authToken');
    
    if (!userId || !authToken) {
        console.error('Not authenticated');
        return;
    }
    
    try {
        const response = await fetch(`/api/preferences/${userId}`, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'Authorization': `Bearer ${authToken}`
            },
            body: JSON.stringify({
                communicationStyle: currentSettings.communicationStyle,
                promptTime: currentSettings.dailyPromptTime,
                timezone: currentSettings.timeZone
            })
        });
        
        if (response.ok) {
            // Update localStorage for immediate use
            localStorage.setItem('scheduledTime', currentSettings.dailyPromptTime);
            localStorage.setItem('theme', currentSettings.theme);
            
            console.log('Settings saved successfully');
        } else {
            console.error('Failed to save settings');
        }
    } catch (error) {
        console.error('Error saving settings:', error);
    }
}

// Toggle response style
function toggleResponseStyle() {
    const styles = ['concise', 'balanced', 'detailed'];
    const currentIndex = styles.indexOf(currentSettings.communicationStyle);
    const nextIndex = (currentIndex + 1) % styles.length;
    currentSettings.communicationStyle = styles[nextIndex];
    
    updateSettingsUI();
    saveSettings();
}

// Toggle daily prompt time
function togglePromptTime() {
    // Create a time picker modal
    const modal = document.createElement('div');
    modal.innerHTML = `
        <div style="position: fixed; top: 0; left: 0; right: 0; bottom: 0; background: rgba(0,0,0,0.8); z-index: 10000; display: flex; align-items: center; justify-content: center;">
            <div style="background: #161b22; border: 1px solid #30363d; border-radius: 12px; padding: 24px; width: 300px;">
                <h3 style="color: #e6edf3; margin-bottom: 16px;">Set Daily Prompt Time</h3>
                <input type="time" id="timePickerInput" value="${currentSettings.dailyPromptTime}" style="
                    width: 100%;
                    padding: 12px;
                    background: #0d1117;
                    border: 1px solid #30363d;
                    border-radius: 8px;
                    color: #e6edf3;
                    font-size: 16px;
                ">
                <div style="display: flex; gap: 12px; margin-top: 20px;">
                    <button onclick="cancelTimePicker()" style="
                        flex: 1;
                        padding: 12px;
                        background: transparent;
                        border: 1px solid #30363d;
                        border-radius: 8px;
                        color: #e6edf3;
                        cursor: pointer;
                    ">Cancel</button>
                    <button onclick="saveTimePicker()" style="
                        flex: 1;
                        padding: 12px;
                        background: #FFD700;
                        border: none;
                        border-radius: 8px;
                        color: #0d1117;
                        font-weight: 600;
                        cursor: pointer;
                    ">Save</button>
                </div>
            </div>
        </div>
    `;
    modal.id = 'timePickerModal';
    document.body.appendChild(modal);
}

function cancelTimePicker() {
    const modal = document.getElementById('timePickerModal');
    if (modal) modal.remove();
}

function saveTimePicker() {
    const input = document.getElementById('timePickerInput');
    if (input) {
        currentSettings.dailyPromptTime = input.value;
        updateSettingsUI();
        saveSettings();
    }
    cancelTimePicker();
}

// Toggle time zone
function toggleTimeZone() {
    // For now, just show current timezone
    // Could implement a timezone picker in the future
    alert(`Current timezone: ${currentSettings.timeZone}\n\nTo change your timezone, please update your system settings.`);
}

// Initialize when page loads
document.addEventListener('DOMContentLoaded', () => {
    loadSettings();
    
    // Add click handlers to settings
    const settingGroups = document.querySelectorAll('.setting-value');
    if (settingGroups[0]) settingGroups[0].onclick = toggleResponseStyle;
    if (settingGroups[1]) settingGroups[1].onclick = togglePromptTime;
    if (settingGroups[2]) settingGroups[2].onclick = toggleTimeZone;
    // settingGroups[3] is progress, not clickable
    // settingGroups[4] is theme, already has handler
});

// Functions for global use
window.toggleResponseStyle = toggleResponseStyle;
window.togglePromptTime = togglePromptTime;
window.toggleTimeZone = toggleTimeZone;
window.cancelTimePicker = cancelTimePicker;
window.saveTimePicker = saveTimePicker;
