import React, { useEffect, useState } from 'react';
import { useAppSelector } from '../../hooks/redux';
import { conversationService, ConversationType } from '../../services/conversationService';
import type { Conversation } from '../../store/conversationSlice';
import { format } from 'date-fns';
import { FiX, FiPlus, FiSettings, FiLogOut, FiRefreshCw } from 'react-icons/fi';
import toast from 'react-hot-toast';

interface SidebarProps {
  isOpen: boolean;
  onClose: () => void;
  onConversationSelect: (id: string) => void;
  onNewChat: () => void;
  onQuickAction: (action: string) => void;
  dailyPromptStatus: {
    isComplete: boolean;
    dayNumber: number | null;
  };
}

const Sidebar: React.FC<SidebarProps> = ({
  isOpen,
  onClose,
  onConversationSelect,
  onNewChat,
  onQuickAction,
  dailyPromptStatus
}) => {
  const { user } = useAppSelector(state => state.auth);
  const { currentConversationId } = useAppSelector(state => state.chat);
  const [conversations, setConversations] = useState<Conversation[]>([]);
  const [isLoading, setIsLoading] = useState(false);
  const [hasError, setHasError] = useState(false);
  const [retryCount, setRetryCount] = useState(0);

  useEffect(() => {
    if (isOpen) {
      loadConversations();
    }
  }, [isOpen, retryCount]);

  const loadConversations = async () => {
    setIsLoading(true);
    setHasError(false);
    
    try {
      const token = localStorage.getItem('token');
      const userId = localStorage.getItem('userId');
      
      if (!token || !userId) {
        console.error('Missing auth credentials');
        setHasError(true);
        setIsLoading(false);
        return;
      }

      const response = await conversationService.getRecentConversations();
      
      if (response && Array.isArray(response)) {
        // Process conversations to ensure proper titles
        const processedConversations = response.map((conv: any) => {
          // Ensure proper title formatting based on type
          if (!conv.title || conv.title === 'New Conversation') {
            conv.title = getDefaultTitle(conv.type, conv.metadata);
          }
          return conv;
        });
        
        setConversations(processedConversations as unknown as Conversation[]);
        setHasError(false);
      } else {
        setConversations([]);
      }
    } catch (error: any) {
      console.error('Failed to load conversations:', error);
      setHasError(true);
      
      // Show specific error messages
      if (error?.response?.status === 401) {
        toast.error('Session expired. Please login again.');
      } else if (error?.response?.status === 404) {
        // No conversations yet is not an error
        setConversations([]);
        setHasError(false);
      } else {
        // Generic connection error
        setHasError(true);
      }
    } finally {
      setIsLoading(false);
    }
  };

  const handleRetry = () => {
    setRetryCount(prev => prev + 1);
    toast('Retrying...', { icon: '🔄' });
  };

  const getDefaultTitle = (type?: string, metadata?: any) => {
    const dayNum = metadata?.dayNumber || dailyPromptStatus.dayNumber;
    
    switch (type) {
      case ConversationType.DAILY_PROMPT:
        return `Day ${dayNum || 1} Leadership Prompt`;
      case ConversationType.BURNING_FIRE:
        return metadata?.topic || '🔥 Burning Fire';
      case ConversationType.TINKER_LEVEL:
        return metadata?.topic || '🔧 Tinker Level Issue';
      default:
        return 'Chat Session';
    }
  };

  const getConversationIcon = (type?: string) => {
    switch (type) {
      case ConversationType.DAILY_PROMPT:
        return '📝';
      case ConversationType.BURNING_FIRE:
        return '🔥';
      case ConversationType.TINKER_LEVEL:
        return '🔧';
      default:
        return '💬';
    }
  };

  const formatConversationDate = (dateStr: string) => {
    try {
      const date = new Date(dateStr);
      const today = new Date();
      const yesterday = new Date(today);
      yesterday.setDate(yesterday.getDate() - 1);

      if (date.toDateString() === today.toDateString()) {
        return format(date, 'h:mm a');
      } else if (date.toDateString() === yesterday.toDateString()) {
        return 'Yesterday';
      } else {
        return format(date, 'MMM d');
      }
    } catch {
      return '';
    }
  };

  return (
    <>
      {/* Overlay */}
      {isOpen && (
        <div
          className="fixed inset-0 bg-black bg-opacity-50 z-40 md:hidden"
          onClick={onClose}
        />
      )}

      {/* Sidebar */}
      <div className={`
        fixed left-0 top-0 h-full w-80 bg-white shadow-lg z-50
        transform transition-transform duration-300 ease-in-out
        ${isOpen ? 'translate-x-0' : '-translate-x-full'}
        md:relative md:translate-x-0 md:shadow-none md:border-r
      `}>
        <div className="flex flex-col h-full">
          {/* Header */}
          <div className="p-4 border-b flex items-center justify-between">
            <div className="flex items-center gap-3">
              <div className="w-10 h-10 rounded-full bg-blue-500 text-white flex items-center justify-center font-semibold">
                {(user?.firstName || user?.email || 'U')?.[0]?.toUpperCase() || 'U'}
              </div>
              <div>
                <p className="font-semibold">{user?.firstName || user?.email || 'User'}</p>
                <p className="text-xs text-gray-500">Day {dailyPromptStatus.dayNumber || 1}</p>
              </div>
            </div>
            <button
              onClick={onClose}
              className="p-2 hover:bg-gray-100 rounded-lg md:hidden"
              aria-label="Close sidebar"
              title="Close"
            >
              <FiX size={20} />
            </button>
          </div>

          {/* Quick Actions */}
          <div className="p-4 space-y-2 border-b">
            <h3 className="text-xs font-semibold text-gray-500 uppercase mb-2">Quick Actions</h3>
            
            {!dailyPromptStatus.isComplete && (
              <QuickActionButton
                icon="📝"
                label={`Day ${dailyPromptStatus.dayNumber || 1} Prompt`}
                description="Today's leadership prompt"
                onClick={() => onQuickAction('daily_prompt')}
                variant="primary"
              />
            )}
            
            <QuickActionButton
              icon="🔥"
              label="Burning Fire"
              description="Urgent issue"
              onClick={() => onQuickAction('burning_fire')}
              variant="urgent"
            />
            
            <QuickActionButton
              icon="🔧"
              label="Tinker Level"
              description="Leadership challenge"
              onClick={() => onQuickAction('tinker_level')}
              variant="tinker"
            />
            
            <QuickActionButton
              icon="✅"
              label="Done for Day"
              description="End today's session"
              onClick={() => onQuickAction('done')}
            />
          </div>

          {/* Previous Chats Section */}
          <div className="flex-1 overflow-y-auto p-4">
            <div className="flex items-center justify-between mb-3">
              <h3 className="text-xs font-semibold text-gray-500 uppercase">Previous Chats</h3>
              <div className="flex gap-1">
                {hasError && (
                  <button
                    onClick={handleRetry}
                    className="p-1.5 hover:bg-gray-100 rounded-lg"
                    title="Retry"
                  >
                    <FiRefreshCw size={16} />
                  </button>
                )}
                <button
                  onClick={onNewChat}
                  className="p-1.5 hover:bg-gray-100 rounded-lg"
                  title="New Chat"
                >
                  <FiPlus size={16} />
                </button>
              </div>
            </div>

            {/* Connection Error State */}
            {hasError ? (
              <div className="text-center py-4">
                <p className="text-gray-500 mb-2">Connection issue</p>
                <button
                  onClick={handleRetry}
                  className="px-4 py-2 bg-yellow-400 text-gray-800 rounded-lg font-medium hover:bg-yellow-500 transition"
                >
                  Retry
                </button>
              </div>
            ) : isLoading ? (
              <div className="text-center py-4 text-gray-500">Loading...</div>
            ) : conversations.length === 0 ? (
              <div className="text-center py-4 text-gray-500">
                <p className="mb-2">No previous chats</p>
                <p className="text-xs">Start a conversation above</p>
              </div>
            ) : (
              <div className="space-y-1">
                {conversations.map((conversation) => (
                  <ConversationItem
                    key={conversation.id}
                    conversation={conversation}
                    isActive={conversation.id === currentConversationId}
                    onClick={() => onConversationSelect(conversation.id)}
                    getIcon={getConversationIcon}
                    formatDate={formatConversationDate}
                  />
                ))}
              </div>
            )}
          </div>

          {/* Footer */}
          <div className="p-4 border-t space-y-2">
            <button className="w-full flex items-center gap-3 px-3 py-2 hover:bg-gray-100 rounded-lg transition">
              <FiSettings size={18} />
              <span>Settings</span>
            </button>
            <button 
              onClick={() => {
                localStorage.clear();
                window.location.href = '/login';
              }}
              className="w-full flex items-center gap-3 px-3 py-2 hover:bg-gray-100 rounded-lg transition text-red-600"
            >
              <FiLogOut size={18} />
              <span>Logout</span>
            </button>
          </div>
        </div>
      </div>
    </>
  );
};

const ConversationItem: React.FC<{
  conversation: Conversation;
  isActive: boolean;
  onClick: () => void;
  getIcon: (type?: string) => string;
  formatDate: (date: string) => string;
}> = ({ conversation, isActive, onClick, getIcon, formatDate }) => {
  const icon = getIcon(conversation.type);
  const date = formatDate(conversation.lastMessageTime || (conversation as any).createdAt || '');
  
  return (
    <button
      onClick={onClick}
      className={`
        w-full text-left px-3 py-2 rounded-lg transition
        ${isActive 
          ? 'bg-blue-50 text-blue-700 border-l-2 border-blue-600' 
          : 'hover:bg-gray-100 text-gray-700'
        }
      `}
    >
      <div className="flex items-start gap-2">
        <span className="mt-0.5 text-lg">{icon}</span>
        <div className="flex-1 min-w-0">
          <p className="text-sm font-medium truncate">{conversation.title}</p>
          {date && <p className="text-xs text-gray-400">{date}</p>}
        </div>
      </div>
    </button>
  );
};

const QuickActionButton: React.FC<{
  icon: string;
  label: string;
  description?: string;
  onClick: () => void;
  variant?: 'default' | 'primary' | 'urgent' | 'tinker';
}> = ({ icon, label, description, onClick, variant = 'default' }) => {
  const variantStyles = {
    default: 'bg-gray-100 hover:bg-gray-200 text-gray-700',
    primary: 'bg-green-50 hover:bg-green-100 text-green-700',
    urgent: 'bg-red-50 hover:bg-red-100 text-red-700',
    tinker: 'bg-blue-50 hover:bg-blue-100 text-blue-700',
  };

  return (
    <button
      onClick={onClick}
      className={`
        w-full flex items-start gap-3 px-3 py-2.5 rounded-lg
        transition text-left
        ${variantStyles[variant]}
      `}
    >
      <span className="text-xl mt-0.5">{icon}</span>
      <div className="flex-1">
        <p className="text-sm font-medium">{label}</p>
        {description && (
          <p className="text-xs opacity-75">{description}</p>
        )}
      </div>
    </button>
  );
};

export default Sidebar;