using Microsoft.AspNetCore.Mvc;

namespace TinkerGenie.API.Presentation.Controllers
{
    [ApiController]
    [Route("api/test")]
    public class TestController : ControllerBase
    {
        [HttpGet]
        public IActionResult Get()
        {
            return Ok(new { message = "Test controller works!" });
        }

        [HttpPost]
        public IActionResult Post()
        {
            return Ok(new { message = "Test POST works!" });
        }
    }
}
