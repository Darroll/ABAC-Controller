using System.Collections.Immutable;
using System.Diagnostics;
using AbacController.Core.Constants;
using AbacController.Core.Domain.Audit;
using AbacController.Core.Domain.Classifications;
using AbacController.Core.Domain.Decisions;
using AbacController.Core.Domain.Entitlements;
using AbacController.Core.Domain.Labels;
using AbacController.Core.Domain.Spif;
using AbacController.Core.Interfaces;

namespace AbacController.Pdp;

/// <summary>
/// Evaluates which classifications a subject is permitted to assign.
/// Applies up to four filter layers in order: ACDF clearance, entitlements (opt-in),
/// native policy rules, application scope.
/// </summary>
public sealed class ClassificationQueryEngine : IClassificationQueryEngine
{
    private readonly ISpifRegistry _spifRegistry;
    private readonly IAcdfEvaluator _acdf;
    private readonly IPolicyRepository _policyRepository;
    private readonly IApplicationRepository _appRepository;
    private readonly IAuditWriter _auditWriter;
    private readonly ITenantContext _tenantContext;
    private readonly IEntitlementResolver? _entitlementResolver;

    public ClassificationQueryEngine(
        ISpifRegistry spifRegistry,
        IAcdfEvaluator acdf,
        IPolicyRepository policyRepository,
        IApplicationRepository appRepository,
        IAuditWriter auditWriter,
        ITenantContext tenantContext,
        IEntitlementResolver? entitlementResolver = null)
    {
        _spifRegistry = spifRegistry;
        _acdf = acdf;
        _policyRepository = policyRepository;
        _appRepository = appRepository;
        _auditWriter = auditWriter;
        _tenantContext = tenantContext;
        _entitlementResolver = entitlementResolver;
    }

    public async Task<AllowedClassificationsResult> EvaluateAsync(
        ClassificationAssignmentQuery query,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var traceSteps = query.IncludeTrace ? new List<ClassificationFilterStep>() : null;

        // Step 1: Load application registration (if specified)
        ApplicationRegistration? app = null;
        if (!string.IsNullOrWhiteSpace(query.ApplicationId))
        {
            app = await _appRepository.GetByIdAsync(query.ApplicationId, ct);
        }

        // Step 2: Resolve governing SPIF
        var spifIndex = ResolveSpifIndex(query, app);
        if (spifIndex is null)
        {
            sw.Stop();
            return new AllowedClassificationsResult
            {
                RequestId = query.RequestId,
                PolicyOid = query.PolicyOidOverride ?? app?.DefaultPolicyOid ?? "unknown",
                PolicyName = null,
                ApplicationId = query.ApplicationId,
                TenantId = _tenantContext.TenantId,
                Classifications = [],
                TotalSpifClassifications = 0,
                EvaluationTime = sw.Elapsed,
                Trace = traceSteps is not null ? new ClassificationFilterTrace { Steps = traceSteps } : null
            };
        }

        // Step 3: Extract security clearance from subject properties
        var clearance = ExtractClearance(query.Subject, spifIndex.PolicyOid);

        // Step 4: Get all non-obsolete classifications from SPIF
        var allClassifications = spifIndex.Spif.Classifications
            .Where(c => !c.Obsolete)
            .OrderBy(c => c.Hierarchy)
            .ToList();

        var totalCount = allClassifications.Count;

        // Step 5: Four-layer filtering (entitlements are opt-in)
        var allowed = new List<AllowedClassification>();

        // Load policy sets for Layer 3
        var policySets = await _policyRepository.GetPolicySetsAsync(ct);
        if (!string.IsNullOrWhiteSpace(query.PolicySetId))
        {
            policySets = policySets.Where(ps => ps.Id == query.PolicySetId).ToList();
        }

        // Resolve entitlements for Layer 2 (skipped when caller has not opted in)
        ResolvedEntitlements? entitlements = null;
        if (query.EnforceEntitlements)
        {
            if (_entitlementResolver is null)
            {
                throw new InvalidOperationException(
                    "ClassificationAssignmentQuery.EnforceEntitlements is true but no IEntitlementResolver was configured.");
            }

            var subject = new EntitlementSubject
            {
                SubjectId = query.Subject.Id,
                TenantId = _tenantContext.TenantId,
                GroupIds = ExtractGroups(query.Subject)
            };
            entitlements = await _entitlementResolver.ResolveAsync(subject, ct);
        }

        foreach (var classification in allClassifications)
        {
            // Layer 1: ACDF Clearance Filter
            if (clearance is not null)
            {
                var acdfResult = EvaluateClearanceFilter(classification, clearance, spifIndex);
                traceSteps?.Add(new ClassificationFilterStep
                {
                    ClassificationName = classification.Name,
                    Lacv = classification.Lacv.Value,
                    FilterLayer = "clearance_filter",
                    Passed = acdfResult,
                    Reason = acdfResult
                        ? $"Clearance permits hierarchy {classification.Hierarchy}"
                        : $"Clearance does not dominate hierarchy {classification.Hierarchy}"
                });

                if (!acdfResult)
                    continue;
            }
            else
            {
                // No clearance provided — fail-closed, allow nothing
                traceSteps?.Add(new ClassificationFilterStep
                {
                    ClassificationName = classification.Name,
                    Lacv = classification.Lacv.Value,
                    FilterLayer = "clearance_filter",
                    Passed = false,
                    Reason = "No security clearance provided — fail-closed"
                });
                continue;
            }

            // Layer 2: Entitlement Filter (opt-in via query.EnforceEntitlements)
            if (entitlements is not null)
            {
                var permits = entitlements.Permits(spifIndex.PolicyOid, classification.Lacv.Value);
                traceSteps?.Add(new ClassificationFilterStep
                {
                    ClassificationName = classification.Name,
                    Lacv = classification.Lacv.Value,
                    FilterLayer = "entitlement_filter",
                    Passed = permits,
                    Reason = permits
                        ? "Subject has a baseline, group, or user grant for this classification"
                        : "No entitlement grant covers this classification (or a deny applies)"
                });

                if (!permits)
                    continue;
            }

            // Layer 3: Native Policy Filter
            var policyResult = EvaluatePolicyFilter(query, classification, policySets);
            traceSteps?.Add(new ClassificationFilterStep
            {
                ClassificationName = classification.Name,
                Lacv = classification.Lacv.Value,
                FilterLayer = "policy_filter",
                Passed = policyResult.Passed,
                Reason = policyResult.Reason
            });

            if (!policyResult.Passed)
                continue;

            // Layer 3: Application Scope Filter
            if (app is not null && app.IsActive)
            {
                var appResult = EvaluateApplicationFilter(classification, app);
                traceSteps?.Add(new ClassificationFilterStep
                {
                    ClassificationName = classification.Name,
                    Lacv = classification.Lacv.Value,
                    FilterLayer = "application_filter",
                    Passed = appResult.Passed,
                    Reason = appResult.Reason
                });

                if (!appResult.Passed)
                    continue;
            }

            // Step 6: Build allowed classification with optional categories
            var allowedClassification = BuildAllowedClassification(
                classification, spifIndex, clearance, app,
                query.IncludeMarkingData, query.IncludeCategories);
            allowed.Add(allowedClassification);
        }

        sw.Stop();

        // Step 7: Write audit event
        _auditWriter.Write(new AuditEvent
        {
            EventType = "classification_query",
            ActionName = query.ActionName,
            SubjectId = query.Subject.Id,
            SubjectType = query.Subject.Type,
            ResourceType = query.Resource?.Type ?? "classification",
            ResourceId = query.Resource?.Id,
            Decision = "Permit",
            TenantId = _tenantContext.TenantId,
            DetailJson = $"{{\"allowed\":{allowed.Count},\"total\":{totalCount},\"applicationId\":\"{query.ApplicationId ?? "(none)"}\"}}"
        });

        return new AllowedClassificationsResult
        {
            RequestId = query.RequestId,
            PolicyOid = spifIndex.PolicyOid,
            PolicyName = spifIndex.PolicyName,
            ApplicationId = query.ApplicationId,
            TenantId = _tenantContext.TenantId,
            Classifications = allowed,
            TotalSpifClassifications = totalCount,
            EvaluationTime = sw.Elapsed,
            Trace = traceSteps is not null ? new ClassificationFilterTrace { Steps = traceSteps } : null
        };
    }

    public async Task<List<AllowedClassificationsResult>> EvaluateBatchAsync(
        List<ClassificationAssignmentQuery> queries,
        CancellationToken ct = default)
    {
        var results = new List<AllowedClassificationsResult>(queries.Count);
        foreach (var query in queries)
        {
            results.Add(await EvaluateAsync(query, ct));
        }
        return results;
    }

    private ISpifIndex? ResolveSpifIndex(ClassificationAssignmentQuery query, ApplicationRegistration? app)
    {
        // Priority: explicit override > application default > registry default
        if (!string.IsNullOrWhiteSpace(query.PolicyOidOverride))
        {
            return _spifRegistry.GetByPolicyOid(query.PolicyOidOverride);
        }

        if (app is not null && !string.IsNullOrWhiteSpace(app.DefaultPolicyOid))
        {
            var appSpif = _spifRegistry.GetByPolicyOid(app.DefaultPolicyOid);
            if (appSpif is not null) return appSpif;
        }

        return _spifRegistry.GetDefault();
    }

    private static SecurityClearance? ExtractClearance(SubjectInfo subject, string policyOid)
    {
        if (!subject.Properties.TryGetValue("securityClearance", out var raw) || raw is null)
            return null;

        if (raw is SecurityClearance clearance)
            return clearance;

        // Try to parse from JSON element (matches AuthZenController pattern)
        return null;
    }

    /// <summary>
    /// Extracts directory group identifiers from subject properties. Callers may pass
    /// them as a <c>IEnumerable&lt;string&gt;</c> under the "groups" or "memberOf" key.
    /// Returns an empty list when no groups are present.
    /// </summary>
    private static IReadOnlyList<string> ExtractGroups(SubjectInfo subject)
    {
        foreach (var key in new[] { "groups", "memberOf" })
        {
            if (!subject.Properties.TryGetValue(key, out var raw) || raw is null)
                continue;

            switch (raw)
            {
                case IEnumerable<string> strings:
                    return strings.ToList();
                case string single when !string.IsNullOrWhiteSpace(single):
                    return [single];
                case System.Collections.IEnumerable enumerable:
                    var list = new List<string>();
                    foreach (var item in enumerable)
                    {
                        if (item is string s && !string.IsNullOrWhiteSpace(s))
                            list.Add(s);
                    }
                    if (list.Count > 0) return list;
                    break;
            }
        }
        return [];
    }

    private static bool EvaluateClearanceFilter(
        SecurityClassification classification,
        SecurityClearance clearance,
        ISpifIndex spifIndex)
    {
        // For allowed-classifications queries, we check hierarchy dominance only.
        // We deliberately skip the full ACDF label validation because:
        // 1) We don't have a complete label (no categories selected yet)
        // 2) Required-category constraints on classifications are handled at label
        //    creation time, not at classification-listing time
        // 3) We're asking "could this user potentially use this classification?"
        //    not "is this specific label valid?"

        var maxClearanceHierarchy = int.MinValue;
        var foundClearanceClassification = false;

        foreach (var clearanceLacv in clearance.ClassificationLacvs)
        {
            if (!spifIndex.TryGetHierarchy(clearanceLacv, out var hierarchy))
                continue;

            foundClearanceClassification = true;
            if (hierarchy > maxClearanceHierarchy)
                maxClearanceHierarchy = hierarchy;
        }

        if (!foundClearanceClassification)
            return false;

        return maxClearanceHierarchy >= classification.Hierarchy;
    }

    private static (bool Passed, string Reason) EvaluatePolicyFilter(
        ClassificationAssignmentQuery query,
        SecurityClassification classification,
        List<Core.Domain.Policy.PolicySet> policySets)
    {
        if (policySets.Count == 0)
            return (true, "No active policy sets — classification allowed by default");

        // Build an evaluation request for this classification
        var request = new EvaluationRequest
        {
            RequestId = query.RequestId,
            Subject = query.Subject,
            Action = new ActionInfo { Name = query.ActionName },
            Resource = new ResourceInfo
            {
                Type = "classification",
                Id = classification.Name,
                Properties = new Dictionary<string, object?>
                {
                    ["classificationLacv"] = classification.Lacv.Value,
                    ["classificationHierarchy"] = classification.Hierarchy,
                    ["classificationName"] = classification.Name
                }
            },
            Context = query.Environment.Count > 0
                ? new ContextInfo { Environment = query.Environment }
                : null
        };

        // Add application context if present
        if (!string.IsNullOrWhiteSpace(query.ApplicationId))
        {
            request.Resource.Properties["applicationId"] = query.ApplicationId;
        }

        // Add resource context if present
        if (query.Resource is not null)
        {
            request.Resource.Properties["sourceResourceType"] = query.Resource.Type;
            request.Resource.Properties["sourceResourceId"] = query.Resource.Id;
        }

        var outcome = NativePolicyEvaluator.Evaluate(request, policySets, null);

        return outcome.Decision switch
        {
            Decision.Deny => (false, $"Denied by policy: {outcome.Message}"),
            Decision.Permit => (true, $"Permitted by policy: {string.Join(", ", outcome.AppliedPolicies)}"),
            Decision.NotApplicable => (true, "No applicable policy — classification allowed by default"),
            Decision.Indeterminate => (false, $"Policy evaluation indeterminate: {outcome.Message}"),
            _ => (true, "Unknown decision — defaulting to allowed")
        };
    }

    private static (bool Passed, string Reason) EvaluateApplicationFilter(
        SecurityClassification classification,
        ApplicationRegistration app)
    {
        // Check LACV whitelist
        if (app.AllowedClassificationLacvs.Count > 0 &&
            !app.AllowedClassificationLacvs.Contains(classification.Lacv.Value))
        {
            return (false, $"Classification LACV {classification.Lacv.Value} not in application whitelist");
        }

        // Check hierarchy ceiling
        if (app.MaxClassificationHierarchy.HasValue &&
            classification.Hierarchy > app.MaxClassificationHierarchy.Value)
        {
            return (false, $"Classification hierarchy {classification.Hierarchy} exceeds application ceiling {app.MaxClassificationHierarchy.Value}");
        }

        return (true, "Classification passes application scope filter");
    }

    private static AllowedClassification BuildAllowedClassification(
        SecurityClassification classification,
        ISpifIndex spifIndex,
        SecurityClearance clearance,
        ApplicationRegistration? app,
        bool includeMarkingData,
        bool includeCategories)
    {
        List<AllowedCategoryTagSet>? allowedCategories = null;

        if (includeCategories)
        {
            allowedCategories = [];
            var tagSetOids = spifIndex.GetTagSetOids();

            foreach (var tagSetOid in tagSetOids)
            {
                // Filter by application tag set whitelist
                if (app is not null && app.AllowedTagSetOids.Count > 0 &&
                    !app.AllowedTagSetOids.Contains(tagSetOid))
                {
                    continue;
                }

                var tagSetIndex = spifIndex.GetTagSet(tagSetOid);
                if (tagSetIndex is null) continue;

                var clearanceTagSet = clearance.GetTagSet(tagSetOid);

                var allowedTags = new List<AllowedCategoryTag>();
                foreach (var spifTag in tagSetIndex.Tags)
                {
                    var allowedCats = BuildAllowedCategories(spifTag, clearanceTagSet);
                    if (allowedCats.Count > 0)
                    {
                        allowedTags.Add(new AllowedCategoryTag
                        {
                            Name = spifTag.Name,
                            TagType = spifTag.TagType,
                            Categories = allowedCats
                        });
                    }
                }

                if (allowedTags.Count > 0)
                {
                    allowedCategories.Add(new AllowedCategoryTagSet
                    {
                        TagSetOid = tagSetOid,
                        Name = tagSetIndex.Name,
                        Tags = allowedTags
                    });
                }
            }
        }

        return new AllowedClassification
        {
            Name = classification.Name,
            Lacv = classification.Lacv.Value,
            Hierarchy = classification.Hierarchy,
            FgColor = classification.FgColor ?? classification.Color,
            BgColor = classification.BgColor,
            MarkingData = includeMarkingData ? classification.MarkingData : null,
            AllowedCategories = allowedCategories,
            RequiredCategories = classification.RequiredCategories,
            ExcludedCategories = classification.ExcludedCategories
        };
    }

    private static List<AllowedCategory> BuildAllowedCategories(
        SecurityCategoryTag spifTag,
        ClearanceCategoryTagSet? clearanceTagSet)
    {
        var allowed = new List<AllowedCategory>();

        foreach (var category in spifTag.Categories)
        {
            // Skip expired or not-yet-valid categories
            var now = DateTimeOffset.UtcNow;
            if (category.NotBefore.HasValue && now < category.NotBefore.Value) continue;
            if (category.NotAfter.HasValue && now > category.NotAfter.Value) continue;

            // Check clearance has this category
            if (clearanceTagSet is not null)
            {
                var clearanceTag = clearanceTagSet.Tags
                    .FirstOrDefault(t => t.TagOid == spifTag.Name || t.TagType == spifTag.TagType);

                if (clearanceTag is not null)
                {
                    var hasCategory = spifTag.TagType switch
                    {
                        TagType.Restrictive => clearanceTag.Bits.Contains(category.Lacv),
                        TagType.Permissive => clearanceTag.Bits.Contains(category.Lacv),
                        TagType.Enumerated => clearanceTag.EnumeratedValues.Contains(category.Lacv),
                        _ => true
                    };

                    if (!hasCategory) continue;
                }
                else
                {
                    // No clearance tag for this type — for restrictive tags, skip;
                    // for permissive/informative, allow
                    if (spifTag.TagType == TagType.Restrictive) continue;
                }
            }

            allowed.Add(new AllowedCategory
            {
                Name = category.Name,
                Lacv = category.Lacv.Value
            });
        }

        return allowed;
    }
}
