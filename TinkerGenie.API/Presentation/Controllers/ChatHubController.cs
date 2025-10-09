using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace TinkerGenie.API.Presentation.Controllers
{
    [ApiController]
    [Route("chatHub")]
    public class ChatHubController : ControllerBase
    {
        [HttpPost("negotiate")]
        public IActionResult Negotiate()
        {
            return Ok(new { 
                connectionId = Guid.NewGuid().ToString(),
                availableTransports = new[] { 
                    new { transport = "WebSockets", transferFormats = new[] { "Text", "Binary" } }
                }
            });
        }
    }
}
