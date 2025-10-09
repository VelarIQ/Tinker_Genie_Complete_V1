using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using System.Text.Json;
using Npgsql;
using Microsoft.Extensions.Logging;

namespace TinkerGenie.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CurriculumController : ControllerBase
    {
        private readonly ILogger<CurriculumController> _logger;
        private readonly string _connectionString;

        public CurriculumController(
            ILogger<CurriculumController> logger, 
            IConfiguration configuration)
        {
            _logger = logger;
            _connectionString = configuration.GetConnectionString("DefaultConnection") ?? string.Empty;
        }

        // Other curriculum-related endpoints can be added here
    }
}
