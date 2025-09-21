import re

# Read the file
with open('Hubs/ChatHub.cs', 'r') as f:
    content = f.read()

# Find and update HandleBurningFiresRequest method
pattern = r'(private async Task<ChatResponse> HandleBurningFiresRequest\([^)]+\)\s*\{[^}]*?// Extract the actual issue[^}]*?\})'

def replacement(match):
    return '''private async Task<ChatResponse> HandleBurningFiresRequest(string userId, string message, string? conversationId)
        {
            try
            {
                _logger.LogInformation("Handling burning fires request with enhanced prompts");
                
                // Use enhanced prompt service if available
                if (_enhancedPromptService != null)
                {
                    var enhancedResponse = await _enhancedPromptService.HandleBurningFire(userId, message);
                    
                    // Create new conversation for burning fire
                    var newConversationId = Guid.NewGuid().ToString();
                    
                    return new ChatResponse
                    {
                        Response = enhancedResponse.FormattedResponse,
                        ConversationId = newConversationId,
                        IsNewThread = true,
                        ThreadType = "burning_fire"
                    };
                }
                
                // Fallback to original implementation
                var issue = message.Replace("burning fire", "", StringComparison.OrdinalIgnoreCase).Trim();
                var response = $"🔥 I understand this is urgent. Let me help you with: {issue}\\n\\n" +
                              "Here are immediate actions you can take...";
                
                return new ChatResponse
                {
                    Response = response,
                    ConversationId = Guid.NewGuid().ToString(),
                    IsNewThread = true,
                    ThreadType = "burning_fire"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling burning fires request");
                return new ChatResponse
                {
                    Response = "I understand this is urgent. Tell me more about the situation.",
                    IsError = true
                };
            }
        }'''

# Apply the replacement
content = re.sub(pattern, replacement, content, flags=re.DOTALL)

# Write back
with open('Hubs/ChatHub.cs', 'w') as f:
    f.write(content)

print("HandleBurningFiresRequest updated")
