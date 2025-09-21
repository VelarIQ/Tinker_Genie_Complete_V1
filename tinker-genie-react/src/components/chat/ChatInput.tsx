import React, { useState, useRef, useEffect, forwardRef } from 'react';
import { FiSend, FiMic, FiMicOff } from 'react-icons/fi';

interface ChatInputProps {
  value: string;
  onChange: (value: string) => void;
  onSend: () => void;
  onDictationToggle?: () => void;
  isDictating?: boolean;
  isLoading?: boolean;
  placeholder?: string;
  quickActions?: Array<{ label: string; action: () => void }>;
}

const ChatInput = forwardRef<HTMLTextAreaElement, ChatInputProps>(({
  value,
  onChange,
  onSend,
  onDictationToggle,
  isDictating = false,
  isLoading = false,
  placeholder = 'Type your message...',
  quickActions
}, ref) => {
  const [isFocused, setIsFocused] = useState(false);
  const [charCount, setCharCount] = useState(0);
  const textareaRef = useRef<HTMLTextAreaElement>(null);

  // Use forwarded ref or local ref
  const inputRef = (ref as React.RefObject<HTMLTextAreaElement>) || textareaRef;

  useEffect(() => {
    setCharCount(value.length);
    adjustTextareaHeight();
  }, [value]);

  const adjustTextareaHeight = () => {
    if (inputRef.current) {
      inputRef.current.style.height = 'auto';
      inputRef.current.style.height = `${Math.min(inputRef.current.scrollHeight, 120)}px`;
    }
  };

  const handleKeyDown = (e: React.KeyboardEvent) => {
    if (e.key === 'Enter' && !e.shiftKey) {
      e.preventDefault();
      if (value.trim() && !isLoading) {
        onSend();
      }
    }
  };

  const handleFocus = () => {
    setIsFocused(true);
    // iOS keyboard handling - scroll into view
    setTimeout(() => {
      if (inputRef.current) {
        inputRef.current.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
      }
    }, 100);
  };

  const hasText = value.trim().length > 0;
  const shouldShowCharCount = charCount > 200;
  const isNearLimit = charCount > 450;
  const isAtLimit = charCount >= 500;

  return (
    <div 
      className={`
        fixed bottom-0 left-0 right-0 bg-white border-t border-gray-200 
        transition-all duration-200 z-40
        ${isFocused ? 'pb-safe' : ''}
      `}
      style={{ paddingBottom: 'env(safe-area-inset-bottom)' }}
    >
      {/* Quick Actions - show when input is empty */}
      {!value && quickActions && quickActions.length > 0 && (
        <div className="px-4 py-2 flex gap-2 overflow-x-auto">
          {quickActions.map((action, index) => (
            <button
              key={index}
              onClick={action.action}
              className="px-3 py-1.5 text-sm bg-gray-100 hover:bg-gray-200 rounded-full whitespace-nowrap transition-colors"
            >
              {action.label}
            </button>
          ))}
        </div>
      )}

      <div className="flex items-end gap-2 px-4 py-3">
        {/* Dictation Button */}
        {onDictationToggle && (
          <button
            onClick={onDictationToggle}
            className={`
              p-2.5 rounded-full transition-all min-w-[44px] min-h-[44px]
              ${isDictating 
                ? 'bg-red-500 text-white animate-pulse' 
                : 'bg-gray-100 hover:bg-gray-200 text-gray-600'
              }
            `}
            aria-label={isDictating ? 'Stop dictation' : 'Start dictation'}
          >
            {isDictating ? <FiMicOff size={20} /> : <FiMic size={20} />}
          </button>
        )}

        {/* Text Input */}
        <div className="flex-1 relative">
          <textarea
            ref={inputRef}
            value={value}
            onChange={(e) => onChange(e.target.value)}
            onKeyDown={handleKeyDown}
            onFocus={handleFocus}
            onBlur={() => setIsFocused(false)}
            placeholder={placeholder}
            disabled={isLoading || isDictating}
            maxLength={500}
            rows={1}
            className={`
              w-full px-4 py-2.5 pr-12 bg-gray-50 border border-gray-200 rounded-lg
              focus:outline-none focus:border-blue-500 focus:bg-white
              resize-none overflow-y-auto transition-all
              disabled:opacity-50 disabled:cursor-not-allowed
              text-[16px] leading-normal
            `}
            style={{
              minHeight: '44px',
              maxHeight: '120px'
            }}
          />
          
          {/* Character Count */}
          {shouldShowCharCount && (
            <div className={`
              absolute bottom-1 right-12 text-xs
              ${isAtLimit ? 'text-red-500' : isNearLimit ? 'text-orange-500' : 'text-gray-400'}
            `}>
              {charCount}/500
            </div>
          )}
        </div>

        {/* Send Button */}
        <button
          onClick={onSend}
          disabled={!hasText || isLoading}
          className={`
            p-2.5 rounded-full transition-all duration-200 min-w-[44px] min-h-[44px]
            ${hasText 
              ? 'bg-yellow-400 hover:bg-yellow-500 text-gray-800 scale-100 animate-pulse-subtle' 
              : 'bg-gray-100 text-gray-400 scale-95'
            }
            ${isLoading ? 'opacity-50 cursor-not-allowed' : ''}
            disabled:cursor-not-allowed
            active:scale-90
          `}
          aria-label="Send message"
        >
          {isLoading ? (
            <div className="animate-spin rounded-full h-5 w-5 border-2 border-gray-800 border-t-transparent" />
          ) : (
            <FiSend size={20} className={hasText ? 'translate-x-0.5' : ''} />
          )}
        </button>
      </div>

      {/* iOS Keyboard Spacer */}
      <div className="h-0 w-full" id="keyboard-spacer" />
    </div>
  );
});

ChatInput.displayName = 'ChatInput';

export default ChatInput;