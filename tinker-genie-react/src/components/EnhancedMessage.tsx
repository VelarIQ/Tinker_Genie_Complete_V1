// Enhanced Message Component for React - Handles clickable module links
import React from 'react';
import ReactMarkdown from 'react-markdown';
import './EnhancedMessage.css';

interface MessageProps {
    content: string;
    role: 'user' | 'assistant';
    sessionType?: 'burning_fires' | 'tinker_level' | 'daily_prompt' | 'general';
    timestamp: Date;
}

/**
 * Enhanced message component that renders Markdown links as clickable buttons
 * Specially formatted for TwoBrain member site links
 */
export const EnhancedMessage: React.FC<MessageProps> = ({ 
    content, 
    role, 
    sessionType,
    timestamp 
}) => {
    
    // Custom link renderer for module links
    const linkRenderer = {
        link: ({ href, children }: any) => {
            const isMemberSiteLink = href?.includes('members.twobrain.com');
            
            if (isMemberSiteLink) {
                return (
                    <a 
                        href={href}
                        target="_blank"
                        rel="noopener noreferrer"
                        className="module-link"
                        onClick={() => {
                            // Track click for analytics
                            trackModuleClick(href, sessionType);
                        }}
                    >
                        <span className="link-icon" aria-hidden>📘</span>
                        <span>{children}</span>
                        <span className="external-icon" aria-hidden>↗️</span>
                    </a>
                );
            }
            
            // Regular link
            return (
                <a 
                    href={href} 
                    target="_blank" 
                    rel="noopener noreferrer"
                    className="regular-link"
                >
                    {children}
                </a>
            );
        },
        
        // Custom rendering for different text elements
        strong: ({ children }: any) => {
            const text = children?.toString() || '';
            
            // Special formatting for section headers
            if (text.includes('Issue Identified:')) {
                return <span className="issue-header">🔥 {text.replace('Issue Identified:', '')}</span>;
            }
            if (text.includes('Strategic Focus:')) {
                return <span className="strategic-header">🔧 {text.replace('Strategic Focus:', '')}</span>;
            }
            if (text.includes('Quick Win:')) {
                return <span className="quick-win">⚡ {text.replace('Quick Win:', '')}</span>;
            }
            if (text.includes('10X Question:')) {
                return <span className="ten-x-question">🚀 {text.replace('10X Question:', '')}</span>;
            }
            if (text.includes('Data Point:')) {
                return <span className="data-point">📊 {text.replace('Data Point:', '')}</span>;
            }
            
            return <strong>{children}</strong>;
        },
        
        // Format lists specially for solutions
        li: ({ children }: any) => {
            const text = children?.toString() || '';
            
            if (sessionType === 'burning_fires' && text.includes('Solution')) {
                return <li className="solution-item">{children}</li>;
            }
            if (sessionType === 'tinker_level') {
                return <li className="strategic-item">{children}</li>;
            }
            
            return <li>{children}</li>;
        }
    };
    
    // Track module clicks for analytics
    const trackModuleClick = (href: string, sessionType?: string) => {
        // Send to analytics
        if ((window as any).gtag) {
            (window as any).gtag('event', 'module_click', {
                module_url: href,
                session_type: sessionType || 'general',
                timestamp: new Date().toISOString()
            });
        }
        
        // Log for debugging
        console.log('Module clicked:', {
            url: href,
            sessionType,
            timestamp: new Date().toISOString()
        });
    };
    
    // Detect session type from content if not provided
    const detectSessionType = (content: string): string => {
        if (content.includes('🔥') && content.includes('Solution')) return 'burning_fires';
        if (content.includes('🔧') && content.includes('Strategic')) return 'tinker_level';
        if (content.includes('Day') && content.includes('prompt')) return 'daily_prompt';
        return 'general';
    };
    
    const effectiveSessionType = sessionType || detectSessionType(content);
    
    return (
        <div className={`message ${role} ${effectiveSessionType}`}>
            <div className="message-content">
                <ReactMarkdown
                    components={linkRenderer}
                    className="markdown-content"
                >
                    {content}
                </ReactMarkdown>
                
                {/* Add session type indicator for burning fires and tinker level */}
                {effectiveSessionType === 'burning_fires' && (
                    <div className="session-indicator burning-fires">
                        <span aria-hidden>⚡</span>
                        <span>Burning Fires Mode</span>
                    </div>
                )}
                
                {effectiveSessionType === 'tinker_level' && (
                    <div className="session-indicator tinker-level">
                        <span aria-hidden>📈</span>
                        <span>Tinker Level Strategy</span>
                    </div>
                )}
            </div>
            
            <div className="message-footer">
                <span className="timestamp">
                    {timestamp.toLocaleTimeString([], { 
                        hour: '2-digit', 
                        minute: '2-digit' 
                    })}
                </span>
            </div>
        </div>
    );
};

// Quick Actions Component for triggering modes
export const QuickActions: React.FC<{
    onBurningFires: () => void;
    onTinkerLevel: () => void;
    currentDay: number;
}> = ({ onBurningFires, onTinkerLevel, currentDay }) => {
    return (
        <div className="quick-actions-bar">
            <button 
                className="quick-action burning-fires-btn"
                onClick={onBurningFires}
                title="Get immediate help with an urgent issue"
            >
                <span aria-hidden>⚡</span>
                <span>Burning Fires</span>
                <span className="action-description">Urgent Issue</span>
            </button>
            
            <button 
                className="quick-action tinker-level-btn"
                onClick={onTinkerLevel}
                title="Strategic planning and long-term thinking"
            >
                <span aria-hidden>📈</span>
                <span>Tinker Level</span>
                <span className="action-description">Strategic Planning</span>
            </button>
            
            <div className="current-day-indicator">
                <span aria-hidden>🎯</span>
                <span>Day {currentDay}/180</span>
            </div>
        </div>
    );
};

export default EnhancedMessage;
