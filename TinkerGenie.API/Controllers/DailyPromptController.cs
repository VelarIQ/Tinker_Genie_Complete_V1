using Microsoft.AspNetCore.Mvc;
using TinkerGenie.API.Services.Interfaces;

namespace TinkerGenie.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DailyPromptController : ControllerBase
    {
        private readonly IWeaviateService _weaviateService;

        public DailyPromptController(IWeaviateService weaviateService)
        {
            _weaviateService = weaviateService;
        }

        [HttpGet("search")]
        public async Task<IActionResult> SearchCurriculum([FromQuery] string query)
        {
            var results = await _weaviateService.SearchLeadershipContentAsync(query, limit: 5);
            return Ok(results);
        }
    }
}
