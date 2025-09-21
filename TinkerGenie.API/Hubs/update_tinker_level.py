import re

# Read the ChatHub file
with open('ChatHub.cs', 'r') as f:
    content = f.read()

# Find the HandleTinkerLevelRequest method
pattern = r'(private async Task<ChatResponse> HandleTinkerLevelRequest\([^)]+\)\s*\{[^}]*?// Extract the strategic challenge[^}]*?\})'

def replacement(match):
    return '''private async Task<ChatResponse> HandleTinkerLevelRequest(string userId, string message, string? conversationId)
        {
            try
            {
                _logger.LogInformation("Handling tinker level request with enhanced prompts");
                
                // Use enhanced prompt service if available
                if (_enhancedPromptService != null)
                {
                    var enhancedResponse = await _enhancedPromptService.HandleTinkerLevel(userId, message);
                    
                    // Create new conversation for tinker level
                    var newConversationId = Guid.NewGuid().ToString();
                    
                    return new ChatResponse
                    {
                        Response = enhancedResponse.FormattedResponse,
                        ConversationId = newConversationId,
                        IsNewThread = true,
                        ThreadType = "tinker_level"
                    };
                }
                
                // Fallback to original implementation
                var challenge = message.Replace("tinker level", "", StringComparison.OrdinalIgnoreCase).Trim();
                var response = $"🔧 Let's work through this strategic challenge: {challenge}\\n\\n" +
                              "Here's a framework to approach this...";
                
                return new ChatResponse
                {
                    Response = response,
                    ConversationId = Guid.NewGuid().ToString(),
                    IsNewThread = true,
                    ThreadType = "tinker_level"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling tinker level request");
                return new ChatResponse
                {
                    Response = "Let's tackle this strategic challenge together. Tell me more.",
                    IsError = true
                };
            }
        }'''

# Apply the replacement
if 'HandleTinkerLevelRequest' in content:
    content = re.sub(pattern, replacement, content, flags=re.DOTALL)
    print("HandleTinkerLevelRequest method updated")
else:
    print("HandleTinkerLevelRequest method not found, adding it...")
    # Find a good place to add it (after HandleBurningFiresRequest)
    insert_point = content.find("private async Task<ChatResponse> HandleDoneForDay")
    if insert_point > 0:
        content = content[:insert_point] + replacement(None) + "\n\n        " + content[insert_point:]
        print("HandleTinkerLevelRequest method added")

# Write back
with open('ChatHub.cs', 'w') as f:
    f.write(content)
