using TinkerGenie.API.Models;

namespace TinkerGenie.API.Services
{
    /// <summary>
    /// September 2025 Multi-Tenant Context Service Interface
    /// Provides tenant context and validation for multi-tenant operations
    /// </summary>
    public interface ITenantContextService
    {
        /// <summary>
        /// Gets the current tenant ID
        /// </summary>
        Guid GetCurrentTenantId();

        /// <summary>
        /// Gets the current tenant information
        /// </summary>
        Task<Tenant?> GetCurrentTenantAsync();

        /// <summary>
        /// Validates if the current tenant has access to a resource
        /// </summary>
        Task<bool> ValidateTenantAccessAsync(Guid resourceTenantId);

        /// <summary>
        /// Sets the tenant context for the current operation
        /// </summary>
        void SetTenantContext(Guid tenantId);
    }
}



