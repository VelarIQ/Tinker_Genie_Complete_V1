using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Text.Json;

namespace TinkerGenie.API.Services
{
    public class WorkerPoolService : BackgroundService
    {
        private readonly ILogger<WorkerPoolService> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly ConcurrentQueue<LLMJob> _jobQueue;
        private readonly SemaphoreSlim _semaphore;
        private readonly int _maxWorkers;

        public WorkerPoolService(ILogger<WorkerPoolService> logger, IServiceProvider serviceProvider)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
            _jobQueue = new ConcurrentQueue<LLMJob>();
            _maxWorkers = Environment.ProcessorCount * 2; // 2x CPU cores
            _semaphore = new SemaphoreSlim(_maxWorkers, _maxWorkers);
        }

        public Task<string> QueueLLMJobAsync(string prompt, string context = "", string jobType = "chat")
        {
            var job = new LLMJob
            {
                Id = Guid.NewGuid().ToString(),
                Prompt = prompt,
                Context = context,
                JobType = jobType,
                CreatedAt = DateTime.UtcNow,
                Status = "queued"
            };

            _jobQueue.Enqueue(job);
            _logger.LogInformation($"Queued LLM job {job.Id} of type {jobType}");

            return Task.FromResult(job.Id);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Worker Pool Service started with {MaxWorkers} workers", _maxWorkers);

            while (!stoppingToken.IsCancellationRequested)
            {
                if (_jobQueue.TryDequeue(out var job))
                {
                    _ = Task.Run(async () => await ProcessJobAsync(job, stoppingToken), stoppingToken);
                }
                else
                {
                    await Task.Delay(100, stoppingToken); // Small delay when no jobs
                }
            }
        }

        private async Task ProcessJobAsync(LLMJob job, CancellationToken cancellationToken)
        {
            await _semaphore.WaitAsync(cancellationToken);
            try
            {
                job.Status = "processing";
                _logger.LogInformation($"Processing LLM job {job.Id}");

                using var scope = _serviceProvider.CreateScope();
                var openAIService = scope.ServiceProvider.GetRequiredService<OpenAIService>();

                string response;
                switch (job.JobType.ToLower())
                {
                    case "chat":
                        response = await openAIService.GenerateResponseAsync(job.Prompt, job.Context);
                        break;
                    case "daily_prompt":
                        response = await openAIService.GenerateResponseAsync(job.Prompt, job.Context);
                        break;
                    case "leadership":
                        response = await openAIService.GenerateResponseAsync(job.Prompt, job.Context);
                        break;
                    default:
                        response = await openAIService.GenerateResponseAsync(job.Prompt, job.Context);
                        break;
                }

                job.Result = response;
                job.Status = "completed";
                job.CompletedAt = DateTime.UtcNow;

                _logger.LogInformation($"Completed LLM job {job.Id} in {(job.CompletedAt.Value - job.CreatedAt).TotalMilliseconds}ms");
            }
            catch (Exception ex)
            {
                job.Status = "failed";
                job.Error = ex.Message;
                job.CompletedAt = DateTime.UtcNow;
                _logger.LogError(ex, "Failed to process LLM job {JobId}", job.Id);
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public async Task<LLMJob?> GetJobResultAsync(string jobId)
        {
            // In a real implementation, you'd store job results in Redis or database
            // For now, return null as this is a simplified version
            return await Task.FromResult<LLMJob?>(null);
        }
    }

    public class LLMJob
    {
        public string Id { get; set; } = string.Empty;
        public string Prompt { get; set; } = string.Empty;
        public string Context { get; set; } = string.Empty;
        public string JobType { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string? Result { get; set; }
        public string? Error { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
    }
}
