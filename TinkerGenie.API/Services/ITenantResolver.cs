using System.Security.Claims;

namespace TinkerGenie.API.Services
{
    /// <summary>
    /// September 2025 Multi-Tenant Resolver Interface
    /// Resolves tenant context without circular dependencies
    /// Designed for 100+ concurrent users with high performance
    /// </summary>
    public interface ITenantResolver
    {
        /// <summary>
        /// Gets the current tenant ID, never returns null (uses default if needed)
        /// </summary>
        Guid GetCurrentTenantId();

        /// <summary>
        /// Tries to get current tenant ID, returns null if not available
        /// </summary>
        Guid? TryGetCurrentTenantId();

        /// <summary>
        /// Sets the tenant ID for the current context (used by middleware)
        /// </summary>
        void SetTenantId(Guid tenantId);

        /// <summary>
        /// Clears the current tenant context
        /// </summary>
        void ClearTenantContext();

        /// <summary>
        /// Gets tenant ID from various sources (JWT, header, subdomain)
        /// </summary>
        Guid? ResolveTenantFromRequest();
    }
}



