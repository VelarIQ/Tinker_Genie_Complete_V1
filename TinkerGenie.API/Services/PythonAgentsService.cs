using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TinkerGenie.API.Services
{
    public interface IPythonAgentsService
    {
        Task<PythonAgentResponse> SendChatAsync(string userId, string message, string? conversationId, string? messageType, string firstName, string businessName);
    }

    public class PythonAgentResponse
    {
        [JsonPropertyName("response")]
        public string Response { get; set; } = "";
        
        [JsonPropertyName("conversation_id")]
        public string ConversationId { get; set; } = "";
        
        [JsonPropertyName("agent_name")]
        public string? AgentName { get; set; }
        
        [JsonPropertyName("message_type")]
        public string? MessageType { get; set; }
        
        [JsonPropertyName("timestamp")]
        public DateTime Timestamp { get; set; }
    }

    public class PythonAgentsService : IPythonAgentsService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<PythonAgentsService> _logger;
        private readonly string _pythonAgentsUrl;

        public PythonAgentsService(HttpClient httpClient, ILogger<PythonAgentsService> logger, IConfiguration configuration)
        {
            _httpClient = httpClient;
            _logger = logger;
            _pythonAgentsUrl = configuration["PythonAgents:Url"] ?? "http://localhost:5001";
            _httpClient.Timeout = TimeSpan.FromSeconds(120);
        }

        public async Task<PythonAgentResponse> SendChatAsync(string userId, string message, string? conversationId, string? messageType, string firstName, string businessName)
        {
            try
            {
                var payload = new { user_id = userId, message, conversation_id = conversationId, message_type = messageType, first_name = firstName, business_name = businessName };
                var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                
                var response = await _httpClient.PostAsync($"{_pythonAgentsUrl}/api/agents/chat", content);
                
                if (!response.IsSuccessStatusCode)
                {
                    var error = await response.Content.ReadAsStringAsync();
                    _logger.LogError($"Python Agents error: {response.StatusCode}, {error}");
                    throw new Exception($"Python Agents service returned {response.StatusCode}");
                }

                var result = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<PythonAgentResponse>(result, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) 
                    ?? throw new Exception("Failed to deserialize response");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calling Python Agents");
                throw;
            }
        }
    }
}
