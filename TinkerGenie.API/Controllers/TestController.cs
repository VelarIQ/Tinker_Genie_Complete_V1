using Microsoft.AspNetCore.Mvc;

namespace TinkerGenie.API.Controllers
{
    [ApiController]
    [Route("api/auth")]
    public class TestValidateController : ControllerBase
    {
        [HttpGet("validate")]
        public IActionResult Validate()
        {
            return Ok(new { isValid = false });
        }
    }
}
