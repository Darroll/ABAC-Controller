using AbacController.Core.Domain.Classifications;

namespace AbacController.Core.Interfaces;

/// <summary>
/// CRUD operations for application registrations.
/// </summary>
public interface IApplicationRepository
{
    /// <summary>Get all application registrations for the current tenant.</summary>
    Task<List<ApplicationRegistration>> GetAllAsync(CancellationToken ct = default);

    /// <summary>Get an application registration by ID.</summary>
    Task<ApplicationRegistration?> GetByIdAsync(string id, CancellationToken ct = default);

    /// <summary>Create or update an application registration.</summary>
    Task<ApplicationRegistration> UpsertAsync(ApplicationRegistration registration, CancellationToken ct = default);

    /// <summary>Delete an application registration by ID.</summary>
    Task<bool> DeleteAsync(string id, CancellationToken ct = default);
}
