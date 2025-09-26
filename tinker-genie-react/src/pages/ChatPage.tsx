import React, { useEffect, useRef, useState, useCallback } from 'react';
import { useAppDispatch, useAppSelector } from '../hooks/redux';
import { sendMessage, startNewChat, loadConversation, checkDailyPrompt } from '../store/chatSlice';
import { conversationService, ConversationType } from '../services/conversationService';
import { signalRService } from '../services/signalRService';
import { storage } from '../utils/storage';
import Sidebar from '../components/layout/Sidebar';
import ChatInput from '../components/chat/ChatInput';
import MessageList from '../components/chat/MessageList';
import DictationPreview from '../components/chat/DictationPreview';
import LoadingSpinner from '../components/common/LoadingSpinner';
import toast from 'react-hot-toast';

const ChatPage: React.FC = () => {
  const dispatch = useAppDispatch();
  const { user } = useAppSelector(state => state.auth);
  const { 
    messages, 
    isLoading, 
    isTyping,
    currentConversationType,
    dailyPrompt,
    currentConversationId
  } = useAppSelector(state => state.chat);

  const [inputValue, setInputValue] = useState('');
  const [isDictating, setIsDictating] = useState(false);
  const [dictationText, setDictationText] = useState('');
  const [showDictationPreview, setShowDictationPreview] = useState(false);
  const [isSidebarOpen, setIsSidebarOpen] = useState(false);

  const messagesEndRef = useRef<HTMLDivElement>(null);
  const inputRef = useRef<HTMLTextAreaElement>(null);
  const recognitionRef = useRef<any>(null);

  // Initialize on mount
  useEffect(() => {
    initializeChat();
    return () => {
      // Cleanup
      if (recognitionRef.current) {
        recognitionRef.current.stop();
      }
    };
  }, []);

  // Auto-scroll to latest message
  useEffect(() => {
    scrollToBottom();
  }, [messages]);

  // Connect SignalR when authenticated
  useEffect(() => {
    const token = storage.getToken();
    if (token) {
      signalRService.connect(token);
    }
    return () => {
      signalRService.disconnect();
    };
  }, []);

  const initializeChat = async () => {
    // Check for daily prompt
    const result = await dispatch(checkDailyPrompt()).unwrap();
    if (result.shouldStart) {
      toast.success(`Welcome! Here's your Day ${result.dayNumber} prompt.`);
    }
  };

  const scrollToBottom = () => {
    messagesEndRef.current?.scrollIntoView({ behavior: 'smooth' });
  };

  const handleSendMessage = useCallback(async () => {
    const messageToSend = inputValue.trim() || dictationText.trim();
    
    if (!messageToSend || isLoading) return;

    // Detect conversation type
    const detection = conversationService.detectConversationType(messageToSend);
    
    // Handle conversation type changes
    if (detection.isExplicitChange) {
      if (detection.type === ConversationType.BURNING_FIRE) {
        // Create new burning fire conversation
        await dispatch(startNewChat(ConversationType.BURNING_FIRE));
        toast('🔥 What\'s the urgent issue?', { icon: '🚨' });
        
        // Clear input and wait for actual issue
        setInputValue('');
        setDictationText('');
        setShowDictationPreview(false);
        inputRef.current?.focus();
        return;
      } else if (detection.type === ConversationType.TINKER_LEVEL) {
        // Create new tinker level conversation
        await dispatch(startNewChat(ConversationType.TINKER_LEVEL));
        toast('🔧 What leadership issue can I help with?', { icon: '🛠️' });
        
        // Clear input and wait for actual issue
        setInputValue('');
        setDictationText('');
        setShowDictationPreview(false);
        inputRef.current?.focus();
        return;
      }
    }

    // Send the message
    try {
      await dispatch(sendMessage(messageToSend)).unwrap();
      setInputValue('');
      setDictationText('');
      setShowDictationPreview(false);
      
      // Send typing indicator
      signalRService.sendTypingIndicator(false, currentConversationId || undefined);
    } catch (error) {
      toast.error('Failed to send message');
    }
  }, [inputValue, dictationText, isLoading, dispatch, currentConversationId]);

  const handleInputChange = (value: string) => {
    setInputValue(value);
    
    // Send typing indicator
    if (currentConversationId) {
      signalRService.sendTypingIndicator(value.length > 0, currentConversationId);
    }
  };

  const handleStartDictation = () => {
    if (!('webkitSpeechRecognition' in window || 'SpeechRecognition' in window)) {
      toast.error('Speech recognition not supported in your browser');
      return;
    }

    if (isDictating && recognitionRef.current) {
      recognitionRef.current.stop();
      setIsDictating(false);
      return;
    }

    const SpeechRecognition = (window as any).webkitSpeechRecognition || (window as any).SpeechRecognition;
    const recognition = new SpeechRecognition();

    recognition.continuous = true;
    recognition.interimResults = true;
    recognition.lang = 'en-US';

    let finalTranscript = '';

    recognition.onstart = () => {
      setIsDictating(true);
      toast('Listening...', { icon: '🎤' });
    };

    recognition.onresult = (event: any) => {
      let interimTranscript = '';
      
      for (let i = event.resultIndex; i < event.results.length; i++) {
        const transcript = event.results[i][0].transcript;
        if (event.results[i].isFinal) {
          finalTranscript += transcript + ' ';
        } else {
          interimTranscript += transcript;
        }
      }

      setDictationText(finalTranscript + interimTranscript);
    };

    recognition.onerror = () => {
      setIsDictating(false);
      toast.error('Speech recognition error');
    };

    recognition.onend = () => {
      setIsDictating(false);
      if (finalTranscript.trim()) {
        setShowDictationPreview(true);
      }
    };

    recognitionRef.current = recognition;
    recognition.start();
  };

  const handleAcceptDictation = () => {
    setInputValue(dictationText);
    setShowDictationPreview(false);
    handleSendMessage();
  };

  const handleCancelDictation = () => {
    setDictationText('');
    setShowDictationPreview(false);
  };

  const handleQuickAction = (action: string) => {
    if (action === 'burning_fire') {
      dispatch(startNewChat(ConversationType.BURNING_FIRE));
      toast('🔥 Starting urgent issue thread', { icon: '🚨' });
    } else if (action === 'tinker_level') {
      dispatch(startNewChat(ConversationType.TINKER_LEVEL));
      toast('🔧 Starting leadership issue thread', { icon: '🛠️' });
    } else if (action === 'daily_prompt' && !dailyPrompt.isComplete) {
      dispatch(startNewChat(ConversationType.DAILY_PROMPT));
    } else if (action === 'done') {
      setInputValue('Done for the day');
      handleSendMessage();
    }
  };

  const handleStartNewChat = () => {
    dispatch(startNewChat(ConversationType.GENERAL_CHAT));
    setInputValue('');
    toast.success('Started new chat');
  };

  const handleConversationSelect = (conversationId: string) => {
    dispatch(loadConversation(conversationId));
    setIsSidebarOpen(false);
  };

  const quickActions = [
    { label: '🔥 Burning Fire', action: () => handleQuickAction('burning_fire') },
    { label: '🔧 Tinker Level', action: () => handleQuickAction('tinker_level') },
    { label: '✅ Done for Day', action: () => handleQuickAction('done') },
  ];

  if (!dailyPrompt.isComplete) {
    quickActions.unshift({ 
      label: `📝 Day ${dailyPrompt.dayNumber || 1} Prompt`, 
      action: () => handleQuickAction('daily_prompt') 
    });
  }

  return (
    <div className="chat-container">
      {/* Mobile Menu Button */}
      <button
        onClick={() => setIsSidebarOpen(!isSidebarOpen)}
        className="fixed top-4 left-4 z-50 p-2 bg-white rounded-lg shadow-md md:hidden"
        aria-label="Toggle menu"
      >
        <svg className="w-6 h-6" fill="none" stroke="currentColor" viewBox="0 0 24 24">
          <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} 
            d={isSidebarOpen ? "M6 18L18 6M6 6l12 12" : "M4 6h16M4 12h16M4 18h16"} />
        </svg>
      </button>

      {/* Sidebar */}
      <Sidebar
        isOpen={isSidebarOpen}
        onClose={() => setIsSidebarOpen(false)}
        onConversationSelect={handleConversationSelect}
        onNewChat={() => handleStartNewChat()}
        onQuickAction={handleQuickAction}
        dailyPromptStatus={dailyPrompt}
      />

      {/* Chat Area */}
      <div className="flex-1 flex flex-col">
        {/* Header */}
        <div className="bg-white border-b px-4 py-3 flex items-center justify-between">
          <div>
            <h2 className="text-lg font-semibold">
              {currentConversationType === ConversationType.DAILY_PROMPT && 'Daily Prompt'}
              {currentConversationType === ConversationType.BURNING_FIRE && '🔥 Burning Fire'}
              {currentConversationType === ConversationType.TINKER_LEVEL && '🔧 Tinker Level'}
              {currentConversationType === ConversationType.GENERAL_CHAT && 'Chat'}
            </h2>
            {dailyPrompt.dayNumber && (
              <p className="text-sm text-gray-600">Day {dailyPrompt.dayNumber}</p>
            )}
          </div>
          
          <button
            onClick={() => handleStartNewChat()}
            className="btn-secondary text-sm"
          >
            New Chat
          </button>
        </div>

        {/* Messages Area */}
        <div className="flex-1 overflow-y-auto px-4 py-4">
          {isLoading && messages.length === 0 ? (
            <div className="flex items-center justify-center h-full">
              <LoadingSpinner size="large" />
            </div>
          ) : (
            <>
              <MessageList 
                messages={messages} 
                isTyping={isTyping}
                currentUser={user}
              />
              <div ref={messagesEndRef} />
            </>
          )}
        </div>

        {/* Dictation Preview */}
        {showDictationPreview && (
          <DictationPreview
            text={dictationText}
            onAccept={handleAcceptDictation}
            onCancel={handleCancelDictation}
            onEdit={(text) => setDictationText(text)}
          />
        )}

        {/* Input Area */}
        <ChatInput
          ref={inputRef}
          value={inputValue}
          onChange={handleInputChange}
          onSend={handleSendMessage}
          onDictationToggle={handleStartDictation}
          isDictating={isDictating}
          isLoading={isLoading}
          placeholder={
            currentConversationType === ConversationType.BURNING_FIRE 
              ? "Describe the urgent issue..." 
              : currentConversationType === ConversationType.TINKER_LEVEL
              ? "Describe the leadership challenge..."
              : "Type your message..."
          }
          quickActions={quickActions}
        />
      </div>
    </div>
  );
};

export default ChatPage;