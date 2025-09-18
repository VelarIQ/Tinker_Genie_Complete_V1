using Microsoft.AspNetCore.Mvc;

namespace TinkerGenie.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TestController : ControllerBase
    {
        [HttpGet]
        public IActionResult Get()
        {
            return Ok(new { 
                status = "API is working",
                time = DateTime.UtcNow,
                message = "Use /api/test/auth for authentication test"
            });
        }

        [HttpPost("auth")]
        public IActionResult TestAuth([FromBody] dynamic request)
        {
            return Ok(new { 
                success = true,
                token = "test-token-" + Guid.NewGuid().ToString(),
                message = "Test auth endpoint working"
            });
        }
    }
}
