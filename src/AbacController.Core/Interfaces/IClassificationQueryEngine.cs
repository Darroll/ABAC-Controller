using AbacController.Core.Domain.Classifications;

namespace AbacController.Core.Interfaces;

/// <summary>
/// Evaluates which classifications a subject is permitted to assign
/// in a given application and context. Applies three-layer filtering:
/// ACDF clearance, native policy rules, and application scope.
/// </summary>
public interface IClassificationQueryEngine
{
    /// <summary>Evaluate allowed classifications for a single query.</summary>
    Task<AllowedClassificationsResult> EvaluateAsync(
        ClassificationAssignmentQuery query,
        CancellationToken ct = default);

    /// <summary>Evaluate allowed classifications for a batch of queries.</summary>
    Task<List<AllowedClassificationsResult>> EvaluateBatchAsync(
        List<ClassificationAssignmentQuery> queries,
        CancellationToken ct = default);
}
