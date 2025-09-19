import React, { useState, useEffect } from 'react';
import { preferencesService } from '../../services/preferencesService';
import toast from 'react-hot-toast';

interface PreferencesModalProps {
  isOpen: boolean;
  onClose: () => void;
  isFirstTime?: boolean;
}

const PreferencesModal: React.FC<PreferencesModalProps> = ({ isOpen, onClose, isFirstTime = false }) => {
  const [communicationStyle, setCommunicationStyle] = useState('');
  const [notificationTime, setNotificationTime] = useState('09:00');
  const [notificationsEnabled, setNotificationsEnabled] = useState(true);
  const [isLoading, setIsLoading] = useState(false);

  useEffect(() => {
    if (isOpen) {
      loadExistingPreferences();
    }
  }, [isOpen]);

  const loadExistingPreferences = async () => {
    const prefs = preferencesService.getCachedPreferences();
    if (prefs) {
      setCommunicationStyle(prefs.communicationStyle || '');
      setNotificationTime(prefs.notificationTime || '09:00');
      setNotificationsEnabled(prefs.notificationsEnabled ?? true);
    }
  };

  const handleSave = async () => {
    if (!communicationStyle) {
      toast.error('Please select your communication style');
      return;
    }

    setIsLoading(true);
    try {
      await preferencesService.updatePreferences({
        communicationStyle,
        notificationTime,
        notificationsEnabled,
        timezone: Intl.DateTimeFormat().resolvedOptions().timeZone,
        language: navigator.language || 'en'
      });

      toast.success('Preferences saved successfully!');
      onClose();
    } catch (error) {
      toast.error('Failed to save preferences');
      console.error('Error saving preferences:', error);
    } finally {
      setIsLoading(false);
    }
  };

  if (!isOpen) return null;

  return (
    <div className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-50">
      <div className="bg-white rounded-lg p-6 max-w-md w-full mx-4">
        <h2 className="text-2xl font-bold mb-4">
          {isFirstTime ? 'Welcome! Let\'s set up your preferences' : 'Update Your Preferences'}
        </h2>
        
        {isFirstTime && (
          <p className="text-gray-600 mb-6">
            This will help me personalize your coaching experience.
          </p>
        )}

        <div className="space-y-4">
          {/* Communication Style */}
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-2">
              Communication Style
            </label>
            <select
              value={communicationStyle}
              onChange={(e) => setCommunicationStyle(e.target.value)}
              className="w-full px-3 py-2 border border-gray-300 rounded-md focus:outline-none focus:ring-2 focus:ring-blue-500"
            >
              <option value="">Select your style...</option>
              <option value="direct">Direct & To-the-point</option>
              <option value="detailed">Detailed & Thorough</option>
              <option value="motivational">Motivational & Encouraging</option>
              <option value="analytical">Analytical & Data-driven</option>
            </select>
          </div>

          {/* Daily Prompt Time */}
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-2">
              Daily Prompt Time
            </label>
            <input
              type="time"
              value={notificationTime}
              onChange={(e) => setNotificationTime(e.target.value)}
              className="w-full px-3 py-2 border border-gray-300 rounded-md focus:outline-none focus:ring-2 focus:ring-blue-500"
            />
            <p className="text-xs text-gray-500 mt-1">
              When would you like to receive your daily leadership prompt?
            </p>
          </div>

          {/* Notifications */}
          <div className="flex items-center">
            <input
              type="checkbox"
              id="notifications"
              checked={notificationsEnabled}
              onChange={(e) => setNotificationsEnabled(e.target.checked)}
              className="h-4 w-4 text-blue-600 focus:ring-blue-500 border-gray-300 rounded"
            />
            <label htmlFor="notifications" className="ml-2 text-sm text-gray-700">
              Enable push notifications for reminders
            </label>
          </div>
        </div>

        <div className="flex gap-3 mt-6">
          {!isFirstTime && (
            <button
              onClick={onClose}
              className="flex-1 px-4 py-2 border border-gray-300 rounded-md text-gray-700 hover:bg-gray-50"
              disabled={isLoading}
            >
              Cancel
            </button>
          )}
          <button
            onClick={handleSave}
            className={`flex-1 px-4 py-2 bg-blue-600 text-white rounded-md hover:bg-blue-700 disabled:opacity-50 ${
              isFirstTime ? 'w-full' : ''
            }`}
            disabled={isLoading || !communicationStyle}
          >
            {isLoading ? 'Saving...' : 'Save Preferences'}
          </button>
        </div>
      </div>
    </div>
  );
};

export default PreferencesModal;