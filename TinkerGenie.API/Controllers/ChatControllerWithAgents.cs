using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using TinkerGenie.API.Services;
using TinkerGenie.API.Hubs;
using TinkerGenie.API.Models;
using System.Security.Claims;

namespace TinkerGenie.API.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/[controller]")]
    public class ChatControllerWithAgents : ControllerBase
    {
        private readonly ILogger<ChatControllerWithAgents> _logger;
        private readonly IPythonAgentsService _pythonAgents;
        private readonly IHubContext<ChatHub> _hubContext;

        public ChatControllerWithAgents(ILogger<ChatControllerWithAgents> logger, IPythonAgentsService pythonAgents, IHubContext<ChatHub> hubContext)
        {
            _logger = logger;
            _pythonAgents = pythonAgents;
            _hubContext = hubContext;
        }

        [HttpPost("send")]
        public async Task<IActionResult> SendMessage([FromBody] ChatRequest request)
        {
            try
            {
                var userId = await ResolveCanonicalUserId(User);
                var firstName = User.FindFirst("first_name")?.Value ?? "User";
                var businessName = User.FindFirst("businessName")?.Value ?? "Your Business";

                _logger.LogInformation($"Processing chat for user {userId}");

                var agentResponse = await _pythonAgents.SendChatAsync(userId, request.Message ?? "", request.ConversationId, request.MessageType, firstName, businessName);

                await _hubContext.Clients.User(userId).SendAsync("ReceiveMessage", new
                {
                    id = Guid.NewGuid().ToString(),
                    content = agentResponse.Response,
                    conversationId = agentResponse.ConversationId,
                    agentName = agentResponse.AgentName,
                    timestamp = agentResponse.Timestamp,
                    sender = "assistant"
                });

                return Ok(new { content = agentResponse.Response, conversationId = agentResponse.ConversationId, agentName = agentResponse.AgentName });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing chat");
                return StatusCode(500, new { message = "Unable to process chat message", error = ex.Message });
            }
        }

        private async Task<string> ResolveCanonicalUserId(ClaimsPrincipal user)
        {
            return user.FindFirst("user_id")?.Value ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? throw new InvalidOperationException("User ID not found");
        }
    }
}
