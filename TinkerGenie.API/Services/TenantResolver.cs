using System.Security.Claims;

namespace TinkerGenie.API.Services
{
    /// <summary>
    /// September 2025 Multi-Tenant Resolver Implementation
    /// High-performance tenant resolution for 100+ concurrent users
    /// NO database dependencies to avoid circular references
    /// </summary>
    public class TenantResolver : ITenantResolver
    {
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ILogger<TenantResolver> _logger;
        
        // Thread-safe tenant context storage
        private static readonly AsyncLocal<Guid?> _currentTenantId = new();
        
        // Default tenant for TwoBrain system
        private static readonly Guid DefaultTenantId = Guid.Parse("58073446-62bd-48ae-840a-6a0f70c21d5f");

        public TenantResolver(
            IHttpContextAccessor httpContextAccessor,
            ILogger<TenantResolver> logger)
        {
            _httpContextAccessor = httpContextAccessor;
            _logger = logger;
        }

        public Guid GetCurrentTenantId()
        {
            return TryGetCurrentTenantId() ?? DefaultTenantId;
        }

        public Guid? TryGetCurrentTenantId()
        {
            // 1. Check thread-local storage first (set by middleware)
            if (_currentTenantId.Value.HasValue)
            {
                return _currentTenantId.Value;
            }

            // 2. Try to resolve from current request
            var resolvedTenantId = ResolveTenantFromRequest();
            if (resolvedTenantId.HasValue)
            {
                _currentTenantId.Value = resolvedTenantId.Value;
                return resolvedTenantId.Value;
            }

            // 3. Return null if no tenant found
            return null;
        }

        public Guid? ResolveTenantFromRequest()
        {
            try
            {
                var httpContext = _httpContextAccessor?.HttpContext;
                if (httpContext == null)
                {
                    return DefaultTenantId;
                }

                // Priority 1: Check HttpContext.Items (set by middleware)
                if (httpContext.Items.TryGetValue("TenantId", out var contextTenantId) && 
                    contextTenantId is Guid tenantFromContext)
                {
                    return tenantFromContext;
                }

                // Priority 2: Check JWT claims (authenticated users)
                if (httpContext.User?.Identity?.IsAuthenticated == true)
                {
                    var tenantClaim = httpContext.User.FindFirst("tenantId")?.Value ??
                                     httpContext.User.FindFirst("tenant_id")?.Value ??
                                     httpContext.User.FindFirst("tid")?.Value;
                    
                    if (!string.IsNullOrEmpty(tenantClaim) && Guid.TryParse(tenantClaim, out var claimTenantId))
                    {
                        // Cache in HttpContext for this request
                        httpContext.Items["TenantId"] = claimTenantId;
                        return claimTenantId;
                    }
                }

                // Priority 3: Check X-Tenant-ID header
                if (httpContext.Request.Headers.TryGetValue("X-Tenant-ID", out var headerValue))
                {
                    var headerTenantId = headerValue.FirstOrDefault();
                    if (!string.IsNullOrEmpty(headerTenantId) && Guid.TryParse(headerTenantId, out var parsedHeaderId))
                    {
                        httpContext.Items["TenantId"] = parsedHeaderId;
                        return parsedHeaderId;
                    }
                }

                // Priority 4: Check subdomain (for future multi-domain support)
                var host = httpContext.Request.Host.Host;
                if (!string.IsNullOrEmpty(host))
                {
                    var tenantFromHost = ExtractTenantFromHost(host);
                    if (tenantFromHost.HasValue)
                    {
                        httpContext.Items["TenantId"] = tenantFromHost.Value;
                        return tenantFromHost.Value;
                    }
                }

                // Priority 5: Default tenant for TwoBrain
                httpContext.Items["TenantId"] = DefaultTenantId;
                return DefaultTenantId;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error resolving tenant from request, using default");
                return DefaultTenantId;
            }
        }

        public void SetTenantId(Guid tenantId)
        {
            _currentTenantId.Value = tenantId;
            
            // Also set in HttpContext if available
            var httpContext = _httpContextAccessor?.HttpContext;
            if (httpContext != null)
            {
                httpContext.Items["TenantId"] = tenantId;
            }
        }

        public void ClearTenantContext()
        {
            _currentTenantId.Value = null;
            
            var httpContext = _httpContextAccessor?.HttpContext;
            if (httpContext != null)
            {
                httpContext.Items.Remove("TenantId");
            }
        }

        private Guid? ExtractTenantFromHost(string host)
        {
            // Future: Extract tenant from subdomain
            // e.g., tenant1.tinker.twobrain.ai -> tenant1
            // For now, return null to use default logic
            
            if (host.StartsWith("tenant1."))
            {
                // Example: return specific tenant ID for tenant1 subdomain
                return Guid.Parse("11111111-1111-1111-1111-111111111111");
            }
            
            return null;
        }
    }
}