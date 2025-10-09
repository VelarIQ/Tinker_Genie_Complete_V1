using Microsoft.AspNetCore.Mvc;
using TinkerGenie.API.Services.Interfaces;

namespace TinkerGenie.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class LearningController : ControllerBase
    {
        private readonly IUserLearningService _userLearningService;
        private readonly IWeaviateService _weaviateService;

        public LearningController(
            IUserLearningService userLearningService,
            IWeaviateService weaviateService)
        {
            _userLearningService = userLearningService;
            _weaviateService = weaviateService;
        }

        [HttpGet("progress")]
        public async Task<IActionResult> GetUserLearningProgress([FromQuery] string userId)
        {
            var progress = await _userLearningService.GetUserLearningProgress(userId);
            return Ok(progress);
        }

        [HttpGet("curriculum")]
        public async Task<IActionResult> SearchCurriculum([FromQuery] string query)
        {
            var results = await _weaviateService.SearchLeadershipContentAsync(query, limit: 5);
            return Ok(results);
        }
    }
}



