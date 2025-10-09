using TinkerGenie.API.Services;

namespace TinkerGenie.API.Middleware
{
    /// <summary>
    /// September 2025 Multi-Tenant Middleware
    /// Resolves and sets tenant context for each request
    /// Optimized for 100+ concurrent users
    /// </summary>
    public class TenantMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<TenantMiddleware> _logger;

        public TenantMiddleware(RequestDelegate next, ILogger<TenantMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context, ITenantResolver tenantResolver)
        {
            try
            {
                // Resolve tenant for this request
                var tenantId = tenantResolver.ResolveTenantFromRequest();
                
                if (tenantId.HasValue)
                {
                    // Set tenant context for this request
                    tenantResolver.SetTenantId(tenantId.Value);
                    
                    // Add tenant info to response headers for debugging (in development)
                    if (context.RequestServices.GetService<IWebHostEnvironment>()?.IsDevelopment() == true)
                    {
                        context.Response.Headers["X-Tenant-ID"] = tenantId.Value.ToString();
                    }
                    
                    _logger.LogDebug("Tenant {TenantId} resolved for request {RequestPath}", 
                        tenantId.Value, context.Request.Path);
                }
                else
                {
                    _logger.LogWarning("No tenant resolved for request {RequestPath}", context.Request.Path);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error resolving tenant for request {RequestPath}", context.Request.Path);
                // Continue processing with default tenant
            }

            // Continue to next middleware
            await _next(context);
            
            // Clean up tenant context after request
            try
            {
                tenantResolver.ClearTenantContext();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error clearing tenant context");
            }
        }
    }

    /// <summary>
    /// Extension method to register TenantMiddleware
    /// </summary>
    public static class TenantMiddlewareExtensions
    {
        public static IApplicationBuilder UseTenantMiddleware(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<TenantMiddleware>();
        }
    }
}