using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using StackExchange.Redis;
using Npgsql;
using System;
using System.Reflection;

namespace TinkerGenie.API.Controllers
{
	[ApiController]
	[Route("api/health")]
	public class HealthController : ControllerBase
	{
		private readonly IConfiguration _configuration;
		private readonly IConnectionMultiplexer _redis;

		public HealthController(IConfiguration configuration, IConnectionMultiplexer redis)
		{
			_configuration = configuration;
			_redis = redis;
		}

		[AllowAnonymous]
		[HttpGet]
		public IActionResult Get()
		{
			var serviceName = Environment.GetEnvironmentVariable("SERVICE_NAME") ?? "TinkerGenie_API";
			var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production";
			var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";
			var startTime = System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime();
			var uptime = DateTime.UtcNow - startTime;

			var status = new
			{
				ok = true,
				name = serviceName,
				environment,
				version,
				timestamp = DateTime.UtcNow,
				uptime = new { totalSeconds = (int)uptime.TotalSeconds }
			};
			return Ok(status);
		}

		[AllowAnonymous]
		[HttpGet("ready")]
		public IActionResult Ready()
		{
			bool redisOk = false;
			bool dbOk = false;
			string? dbError = null;

			try
			{
				var db = _redis.GetDatabase();
				var pong = db.Ping();
				redisOk = pong.TotalMilliseconds >= 0;
			}
			catch { redisOk = false; }

			try
			{
				var cs = _configuration.GetConnectionString("ConnectionString")
					?? _configuration.GetConnectionString("DefaultConnection");
				if (!string.IsNullOrWhiteSpace(cs))
				{
					using var conn = new NpgsqlConnection(cs);
					conn.Open();
					using var cmd = new NpgsqlCommand("SELECT 1", conn);
					var res = cmd.ExecuteScalar();
					dbOk = (res is int i) && i == 1;
				}
				else { dbError = "No connection string configured"; }
			}
			catch (Exception ex) { dbOk = false; dbError = ex.Message; }

			var ok = redisOk && dbOk;
			return Ok(new
			{
				ok,
				dependencies = new
				{
					redis = new { ok = redisOk },
					postgres = new { ok = dbOk, error = dbError }
				}
			});
		}

		[AllowAnonymous]
		[HttpGet("live")]
		public IActionResult Live() { return Ok(new { ok = true }); }

		[AllowAnonymous]
		[HttpHead]
		public IActionResult Head() { return Ok(); }
	}
}



