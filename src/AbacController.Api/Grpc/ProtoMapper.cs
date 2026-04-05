using System.Collections.Immutable;
using System.Text.Json;
using AbacController.Core.Constants;
using AbacController.Core.Domain.Attributes;
using AbacController.Core.Domain.Decisions;
using AbacController.Core.Domain.Labels;
using AbacController.Core.Domain.Policy;
using AbacController.Core.Domain.Spif;
using Google.Protobuf.WellKnownTypes;

namespace AbacController.Api.Grpc;

/// <summary>
/// Maps between protobuf message types and domain model types.
/// </summary>
internal static class ProtoMapper
{
    /// <summary>Converts a protobuf evaluation request into the domain evaluation model.</summary>
    public static EvaluationRequest ToDomain(EvaluateRequestMessage request)
        => new()
        {
            RequestId = EmptyToNull(request.RequestId),
            Subject = ToDomain(request.Subject),
            Action = ToDomain(request.Action),
            Resource = ToDomain(request.Resource),
            Context = request.Context is null ? null : ToDomain(request.Context),
            Options = ToDomain(request.Options)
        };

    /// <summary>Converts a protobuf batch evaluation request into the domain batch model.</summary>
    public static BatchEvaluationRequest ToDomain(EvaluateBatchRequestMessage request)
        => new()
        {
            RequestId = EmptyToNull(request.RequestId),
            Subject = ToDomain(request.Subject),
            Context = request.Context is null ? null : ToDomain(request.Context),
            Options = ToDomain(request.Options),
            Evaluations = request.Evaluations.Select(e => new BatchEvaluation
            {
                EvaluationId = e.EvaluationId,
                Action = ToDomain(e.Action),
                Resource = ToDomain(e.Resource)
            }).ToList()
        };

    /// <summary>Converts a domain evaluation result into its protobuf response representation.</summary>
    public static EvaluateResponseMessage ToProto(EvaluationResult result)
    {
        var response = new EvaluateResponseMessage
        {
            RequestId = result.RequestId ?? string.Empty,
            DecisionId = result.DecisionId,
            Decision = ToProto(result.Decision),
            EvaluationTime = Duration.FromTimeSpan(result.EvaluationTime),
            CacheStatus = result.CacheStatus ?? string.Empty
        };

        if (result.Status is not null)
        {
            response.Status = new StatusMessage
            {
                Code = result.Status.Code,
                Message = result.Status.Message ?? string.Empty
            };
        }

        response.Obligations.AddRange(result.Obligations.Select(o => new ObligationMessage
        {
            Id = o.Id,
            Attributes = ToStruct(o.Attributes)
        }));

        response.Advice.AddRange(result.Advice.Select(a => new AdviceMessage
        {
            Id = a.Id,
            Attributes = ToStruct(a.Attributes)
        }));

        response.AppliedPolicies.AddRange(result.AppliedPolicies);
        response.AttributeProvenance.AddRange(result.AttributeProvenance.Select(p => new AttributeProvenanceMessage
        {
            AttributeName = p.AttributeName,
            Source = p.Source,
            FetchedAt = Timestamp.FromDateTimeOffset(p.FetchedAt),
            SourceTtl = p.SourceTtl.HasValue ? Duration.FromTimeSpan(p.SourceTtl.Value) : null
        }));

        return response;
    }

    /// <summary>Converts a domain batch evaluation result into its protobuf response representation.</summary>
    public static EvaluateBatchResponseMessage ToProto(BatchEvaluationResult result)
    {
        var response = new EvaluateBatchResponseMessage
        {
            RequestId = result.RequestId ?? string.Empty,
            BatchDecisionId = result.BatchDecisionId
        };
        response.Evaluations.AddRange(result.Evaluations.Select(ToProto));
        return response;
    }

    /// <summary>Converts an explained evaluation result into its protobuf response representation.</summary>
    public static ExplainedEvaluateResponseMessage ToProto(ExplainedEvaluationResult result)
    {
        var response = new ExplainedEvaluateResponseMessage
        {
            Result = ToProto(result.Result),
            Trace = new EvaluationTraceMessage
            {
                PolicySetId = result.Trace.PolicySetId ?? string.Empty,
                PolicyVersion = result.Trace.PolicyVersion ?? string.Empty,
                MatchedPolicy = result.Trace.MatchedPolicy ?? string.Empty
            }
        };

        response.Trace.Steps.AddRange(result.Trace.Steps.Select(step => new TraceStepMessage
        {
            RuleId = step.RuleId,
            Effect = step.Effect,
            Result = step.Result,
            Reason = step.Reason
        }));

        return response;
    }

    /// <summary>Converts a domain policy set into a protobuf summary message.</summary>
    public static PolicySetSummaryMessage ToProto(PolicySet policySet)
        => new()
        {
            Id = policySet.Id,
            Name = policySet.Name,
            Description = policySet.Description ?? string.Empty,
            SpifId = policySet.SpifId?.ToString() ?? string.Empty,
            CombiningAlgorithm = policySet.CombiningAlgorithm,
            IsActive = policySet.IsActive,
            CreatedAt = Timestamp.FromDateTimeOffset(policySet.CreatedAt),
            UpdatedAt = Timestamp.FromDateTimeOffset(policySet.UpdatedAt),
            PolicyCount = policySet.Policies.Count
        };

    /// <summary>Converts a domain policy version into a protobuf summary message.</summary>
    public static PolicyVersionSummaryMessage ToProto(PolicyVersion version)
        => new()
        {
            Id = version.Id.ToString(),
            PolicyId = version.PolicyId,
            VersionNumber = version.VersionNumber,
            Hash = version.Hash,
            CreatedBy = version.CreatedBy ?? string.Empty,
            CreatedAt = Timestamp.FromDateTimeOffset(version.CreatedAt),
            IsActive = version.IsActive,
            Content = version.Content
        };

    /// <summary>Converts a protobuf policy-set summary into the domain policy-set model.</summary>
    public static PolicySet ToDomain(PolicySetSummaryMessage message)
        => new()
        {
            Id = message.Id,
            Name = message.Name,
            Description = EmptyToNull(message.Description),
            SpifId = Guid.TryParse(message.SpifId, out var spifId) ? spifId : null,
            CombiningAlgorithm = string.IsNullOrWhiteSpace(message.CombiningAlgorithm)
                ? "deny-overrides"
                : message.CombiningAlgorithm,
            IsActive = message.IsActive,
            CreatedAt = message.CreatedAt?.ToDateTimeOffset() ?? DateTimeOffset.UtcNow,
            UpdatedAt = message.UpdatedAt?.ToDateTimeOffset() ?? DateTimeOffset.UtcNow,
            Policies = []
        };

    /// <summary>Converts a persisted PIP source entity into its protobuf message representation.</summary>
    public static PipSourceMessage ToProto(AbacController.Data.Entities.PipSourceEntity source)
    {
        var message = new PipSourceMessage
        {
            Id = source.Id,
            Name = source.Name,
            SourceType = source.SourceType,
            ConfigJson = source.ConfigJson,
            Priority = source.Priority,
            IsRequired = source.IsRequired,
            CacheEnabled = source.CacheEnabled,
            CacheTtlSeconds = source.CacheTtlSeconds,
            CacheMaxEntries = source.CacheMaxEntries,
            CreatedAt = Timestamp.FromDateTimeOffset(source.CreatedAt),
            UpdatedAt = Timestamp.FromDateTimeOffset(source.UpdatedAt)
        };
        message.ProvidesAttributes.AddRange(SplitCsv(source.ProvidesAttributes));
        return message;
    }

    /// <summary>Converts a protobuf PIP source message into a persisted entity.</summary>
    public static AbacController.Data.Entities.PipSourceEntity ToEntity(PipSourceMessage source)
        => new()
        {
            Id = source.Id,
            Name = source.Name,
            SourceType = source.SourceType,
            ConfigJson = source.ConfigJson,
            ProvidesAttributes = string.Join(',', source.ProvidesAttributes),
            Priority = source.Priority,
            IsRequired = source.IsRequired,
            CacheEnabled = source.CacheEnabled,
            CacheTtlSeconds = source.CacheTtlSeconds,
            CacheMaxEntries = source.CacheMaxEntries,
            CreatedAt = source.CreatedAt?.ToDateTimeOffset() ?? DateTimeOffset.UtcNow,
            UpdatedAt = source.UpdatedAt?.ToDateTimeOffset() ?? DateTimeOffset.UtcNow
        };

    /// <summary>Converts a persisted enforcement-point entity into its protobuf message representation.</summary>
    public static EnforcementPointMessage ToProto(AbacController.Data.Entities.EnforcementPointEntity entity)
    {
        var message = new EnforcementPointMessage
        {
            Id = entity.Id,
            Name = entity.Name,
            Type = entity.Type,
            Endpoint = entity.Endpoint ?? string.Empty,
            EnforcementMode = entity.EnforcementMode,
            SpifId = entity.SpifId ?? string.Empty,
            CreatedAt = Timestamp.FromDateTimeOffset(entity.CreatedAt),
            UpdatedAt = Timestamp.FromDateTimeOffset(entity.UpdatedAt)
        };
        message.PolicySetBindings.AddRange(ParseJsonStringArray(entity.PolicySetBindings));
        return message;
    }

    /// <summary>Converts a protobuf enforcement-point message into a persisted entity.</summary>
    public static AbacController.Data.Entities.EnforcementPointEntity ToEntity(EnforcementPointMessage message)
        => new()
        {
            Id = message.Id,
            Name = message.Name,
            Type = message.Type,
            Endpoint = EmptyToNull(message.Endpoint),
            EnforcementMode = string.IsNullOrWhiteSpace(message.EnforcementMode) ? "enforcing" : message.EnforcementMode,
            PolicySetBindings = JsonSerializer.Serialize(message.PolicySetBindings),
            SpifId = EmptyToNull(message.SpifId),
            CreatedAt = message.CreatedAt?.ToDateTimeOffset() ?? DateTimeOffset.UtcNow,
            UpdatedAt = message.UpdatedAt?.ToDateTimeOffset() ?? DateTimeOffset.UtcNow
        };

    /// <summary>Converts a persisted SPIF entity into a protobuf registration message.</summary>
    public static SpifRegistrationMessage ToProto(AbacController.Data.Entities.SpifEntity entity)
        => new()
        {
            PolicyOid = entity.PolicyOid,
            Name = entity.Name,
            SchemaVersion = entity.SchemaVersion,
            IsActive = entity.IsActive,
            ImportedAt = Timestamp.FromDateTimeOffset(entity.ImportedAt),
            ClassificationCount = entity.ClassificationCount,
            CategoryCount = entity.CategoryCount
        };

    /// <summary>Converts a domain audit event into its protobuf message representation.</summary>
    public static AuditEventMessage ToProto(AbacController.Core.Domain.Audit.AuditEvent auditEvent)
        => new()
        {
            Id = auditEvent.Id.ToString(),
            Timestamp = Timestamp.FromDateTimeOffset(auditEvent.Timestamp),
            EventType = auditEvent.EventType,
            RequestId = auditEvent.RequestId ?? string.Empty,
            DecisionId = auditEvent.DecisionId ?? string.Empty,
            SubjectType = auditEvent.SubjectType ?? string.Empty,
            SubjectId = auditEvent.SubjectId ?? string.Empty,
            ActionName = auditEvent.ActionName ?? string.Empty,
            ResourceType = auditEvent.ResourceType ?? string.Empty,
            ResourceId = auditEvent.ResourceId ?? string.Empty,
            Decision = auditEvent.Decision ?? string.Empty,
            AppliedPolicies = auditEvent.AppliedPolicies ?? string.Empty,
            ObligationsJson = auditEvent.ObligationsJson ?? string.Empty,
            AttributesUsedJson = auditEvent.AttributesUsedJson ?? string.Empty,
            EvaluationTimeMs = auditEvent.EvaluationTimeMs ?? 0,
            PepId = auditEvent.PepId ?? string.Empty,
            ActorIdentity = auditEvent.ActorIdentity ?? string.Empty,
            DetailJson = auditEvent.DetailJson ?? string.Empty
        };

    /// <summary>Converts an attribute-resolution result into its protobuf response representation.</summary>
    public static ResolveAttributesResponseMessage ToProto(AttributeResolutionResult result)
    {
        var response = new ResolveAttributesResponseMessage
        {
            Success = result.Success,
            Error = result.Error ?? string.Empty
        };

        response.Values.AddRange(result.Values.Select(value => new ResolvedAttributeMessage
        {
            Name = value.Name,
            Category = ToProto(value.Category),
            Value = ToValue(value.Value),
            SourceId = value.SourceId,
            SourceType = value.SourceType
        }));
        response.Missing.AddRange(result.Missing);
        return response;
    }

    /// <summary>Converts a protobuf security label into the domain label model.</summary>
    public static SecurityLabel ToDomain(SecurityLabelMessage message)
        => new()
        {
            PolicyOid = EmptyToNull(message.PolicyOid),
            PolicyName = EmptyToNull(message.PolicyName),
            ClassificationLacv = new LacvValue(message.ClassificationLacv),
            ClassificationName = EmptyToNull(message.ClassificationName),
            PrivacyMarks = message.PrivacyMarks.ToImmutableList(),
            CreatedAt = message.CreatedAt is null ? null : message.CreatedAt.ToDateTimeOffset(),
            CategoryTagSets = message.CategoryTagSets.Select(ToDomain).ToImmutableList()
        };

    /// <summary>Converts a protobuf security clearance into the domain clearance model.</summary>
    public static SecurityClearance ToDomain(SecurityClearanceMessage message)
        => new()
        {
            PolicyOid = message.PolicyOid,
            ClassificationLacvs = message.ClassificationLacvs.Select(v => new LacvValue(v)).ToImmutableHashSet(),
            CategoryTagSets = message.CategoryTagSets.Select(ToDomain).ToImmutableList()
        };

    /// <summary>Converts a domain security label into its protobuf message representation.</summary>
    public static SecurityLabelMessage ToProto(SecurityLabel label)
    {
        var message = new SecurityLabelMessage
        {
            PolicyOid = label.PolicyOid ?? string.Empty,
            PolicyName = label.PolicyName ?? string.Empty,
            ClassificationLacv = label.ClassificationLacv.Value,
            ClassificationName = label.ClassificationName ?? string.Empty
        };

        if (label.CreatedAt.HasValue)
        {
            message.CreatedAt = Timestamp.FromDateTimeOffset(label.CreatedAt.Value);
        }

        message.PrivacyMarks.AddRange(label.PrivacyMarks);
        message.CategoryTagSets.AddRange(label.CategoryTagSets.Select(ToProto));
        return message;
    }

    private static SubjectInfo ToDomain(SubjectMessage subject)
    {
        var properties = ToDictionary(subject.Properties);
        if (subject.SecurityClearance is not null && !string.IsNullOrWhiteSpace(subject.SecurityClearance.PolicyOid))
        {
            properties["securityClearance"] = ToDomain(subject.SecurityClearance);
        }

        return new SubjectInfo
        {
            Type = subject.Type,
            Id = subject.Id,
            Properties = properties
        };
    }

    private static ActionInfo ToDomain(ActionMessage action)
        => new()
        {
            Name = action.Name,
            Properties = ToDictionary(action.Properties)
        };

    private static ResourceInfo ToDomain(ResourceMessage resource)
    {
        var properties = ToDictionary(resource.Properties);
        if (resource.SecurityLabel is not null && (resource.SecurityLabel.ClassificationLacv != 0 || !string.IsNullOrWhiteSpace(resource.SecurityLabel.PolicyOid)))
        {
            properties["securityLabel"] = ToDomain(resource.SecurityLabel);
            if (!string.IsNullOrWhiteSpace(resource.SecurityLabel.PolicyOid))
            {
                properties["securityLabel.policyOid"] = resource.SecurityLabel.PolicyOid;
            }
        }

        return new ResourceInfo
        {
            Type = resource.Type,
            Id = resource.Id,
            Properties = properties
        };
    }

    private static ContextInfo ToDomain(ContextMessage context)
        => new()
        {
            Environment = ToDictionary(context.Environment)
        };

    private static EvaluateOptions ToDomain(EvaluateOptionsMessage options)
        => new()
        {
            ReturnObligations = options?.ReturnObligations ?? false,
            ReturnAdvice = options?.ReturnAdvice ?? false,
            PolicySetId = EmptyToNull(options?.PolicySetId),
            PolicyVersion = EmptyToNull(options?.PolicyVersion),
            BypassCache = options?.BypassCache ?? false,
            PolicyIdOverride = EmptyToNull(options?.PolicyIdOverride)
        };

    private static LabelCategoryTagSet ToDomain(LabelCategoryTagSetMessage message)
        => new()
        {
            TagSetOid = message.TagSetOid,
            Tags = message.Tags.Select(ToDomain).ToImmutableList()
        };

    private static LabelCategoryTag ToDomain(LabelCategoryTagMessage message)
        => new()
        {
            Name = EmptyToNull(message.Name),
            TagType = ToDomain(message.TagType),
            EnumType = ToDomainOptional(message.EnumType),
            TagOid = EmptyToNull(message.TagOid),
            Bits = message.Bits.Select(v => new LacvValue(v)).ToImmutableHashSet(),
            EnumeratedValues = message.EnumeratedValues.Select(v => new LacvValue(v)).ToImmutableHashSet(),
            Categories = message.Categories.Select(ToDomain).ToImmutableList()
        };

    private static LabelCategory ToDomain(LabelCategoryMessage message)
        => new()
        {
            Name = message.Name,
            Lacv = new LacvValue(message.Lacv),
            NotBefore = message.NotBefore is null ? null : message.NotBefore.ToDateTimeOffset(),
            NotAfter = message.NotAfter is null ? null : message.NotAfter.ToDateTimeOffset()
        };

    private static ClearanceCategoryTagSet ToDomain(ClearanceCategoryTagSetMessage message)
        => new()
        {
            TagSetOid = message.TagSetOid,
            Tags = message.Tags.Select(ToDomain).ToImmutableList()
        };

    private static ClearanceCategoryTag ToDomain(ClearanceCategoryTagMessage message)
        => new()
        {
            TagOid = EmptyToNull(message.TagOid),
            TagType = ToDomain(message.TagType),
            Bits = message.Bits.Select(v => new LacvValue(v)).ToImmutableHashSet(),
            EnumeratedValues = message.EnumeratedValues.Select(v => new LacvValue(v)).ToImmutableHashSet()
        };

    private static LabelCategoryTagSetMessage ToProto(LabelCategoryTagSet tagSet)
    {
        var message = new LabelCategoryTagSetMessage { TagSetOid = tagSet.TagSetOid };
        message.Tags.AddRange(tagSet.Tags.Select(ToProto));
        return message;
    }

    private static LabelCategoryTagMessage ToProto(LabelCategoryTag tag)
    {
        var message = new LabelCategoryTagMessage
        {
            Name = tag.Name ?? string.Empty,
            TagType = ToProto(tag.TagType),
            EnumType = ToProto(tag.EnumType),
            TagOid = tag.TagOid ?? string.Empty
        };
        message.Bits.AddRange(tag.Bits.Select(bit => bit.Value));
        message.EnumeratedValues.AddRange(tag.EnumeratedValues.Select(value => value.Value));
        message.Categories.AddRange(tag.Categories.Select(cat => new LabelCategoryMessage
        {
            Name = cat.Name,
            Lacv = cat.Lacv.Value,
            NotBefore = cat.NotBefore.HasValue ? Timestamp.FromDateTimeOffset(cat.NotBefore.Value) : null,
            NotAfter = cat.NotAfter.HasValue ? Timestamp.FromDateTimeOffset(cat.NotAfter.Value) : null
        }));
        return message;
    }

    /// <summary>Converts a dictionary of values into a protobuf <see cref="Struct"/>.</summary>
    public static Struct ToStruct(IDictionary<string, object?> values)
    {
        var result = new Struct();
        foreach (var pair in values)
        {
            result.Fields[pair.Key] = ToValue(pair.Value);
        }
        return result;
    }

    /// <summary>Converts a protobuf <see cref="Struct"/> into a dictionary of CLR values.</summary>
    public static Dictionary<string, object?> ToDictionary(Struct? structValue)
    {
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (structValue is null)
        {
            return result;
        }

        foreach (var pair in structValue.Fields)
        {
            result[pair.Key] = FromValue(pair.Value);
        }

        return result;
    }

    /// <summary>Converts a JSON document into a protobuf <see cref="Struct"/>.</summary>
    public static Struct ToStruct(JsonDocument? document)
        => document is null ? new Struct() : Struct.Parser.ParseJson(document.RootElement.GetRawText());

    /// <summary>Converts a CLR value into a protobuf <see cref="Value"/>.</summary>
    public static Value ToValue(object? value)
        => value switch
        {
            null => Value.ForNull(),
            string s => Value.ForString(s),
            bool b => Value.ForBool(b),
            int i => Value.ForNumber(i),
            long l => Value.ForNumber(l),
            float f => Value.ForNumber(f),
            double d => Value.ForNumber(d),
            decimal m => Value.ForString(m.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            DateTimeOffset dto => Value.ForString(dto.ToString("O")),
            IEnumerable<string> strings => new Value { ListValue = new ListValue { Values = { strings.Select(Value.ForString) } } },
            IEnumerable<object?> objects => new Value { ListValue = new ListValue { Values = { objects.Select(ToValue) } } },
            IDictionary<string, object?> dict => new Value { StructValue = ToStruct(dict) },
            JsonElement element => Value.Parser.ParseJson(element.GetRawText()),
            _ => Value.ForString(value.ToString() ?? string.Empty)
        };

    private static object? FromValue(Value value)
        => value.KindCase switch
        {
            Value.KindOneofCase.NullValue => null,
            Value.KindOneofCase.StringValue => value.StringValue,
            Value.KindOneofCase.BoolValue => value.BoolValue,
            Value.KindOneofCase.NumberValue => value.NumberValue,
            Value.KindOneofCase.StructValue => ToDictionary(value.StructValue),
            Value.KindOneofCase.ListValue => value.ListValue.Values.Select(FromValue).ToList(),
            _ => null
        };

    private static DecisionMessage ToProto(Decision decision)
        => decision switch
        {
            Decision.Permit => DecisionMessage.Permit,
            Decision.Deny => DecisionMessage.Deny,
            Decision.NotApplicable => DecisionMessage.NotApplicable,
            Decision.Indeterminate => DecisionMessage.Indeterminate,
            _ => DecisionMessage.Unspecified
        };

    private static AttributeCategoryMessage ToProto(AttributeCategory category)
        => category switch
        {
            AttributeCategory.Subject => AttributeCategoryMessage.Subject,
            AttributeCategory.Resource => AttributeCategoryMessage.Resource,
            AttributeCategory.Action => AttributeCategoryMessage.Action,
            AttributeCategory.Environment => AttributeCategoryMessage.Environment,
            _ => AttributeCategoryMessage.Unspecified
        };

    private static TagType ToDomain(TagTypeMessage tagType)
        => tagType switch
        {
            TagTypeMessage.Restrictive => TagType.Restrictive,
            TagTypeMessage.Permissive => TagType.Permissive,
            TagTypeMessage.Enumerated => TagType.Enumerated,
            TagTypeMessage.TagType7 => TagType.TagType7,
            TagTypeMessage.NotApplicable => TagType.NotApplicable,
            _ => TagType.Restrictive
        };

    private static EnumType? ToDomainOptional(EnumTypeMessage enumType)
        => enumType switch
        {
            EnumTypeMessage.Restrictive => EnumType.Restrictive,
            EnumTypeMessage.Permissive => EnumType.Permissive,
            _ => null
        };

    private static TagTypeMessage ToProto(TagType tagType)
        => tagType switch
        {
            TagType.Restrictive => TagTypeMessage.Restrictive,
            TagType.Permissive => TagTypeMessage.Permissive,
            TagType.Enumerated => TagTypeMessage.Enumerated,
            TagType.TagType7 => TagTypeMessage.TagType7,
            TagType.NotApplicable => TagTypeMessage.NotApplicable,
            _ => TagTypeMessage.Unspecified
        };

    private static EnumTypeMessage ToProto(EnumType? enumType)
        => enumType switch
        {
            EnumType.Restrictive => EnumTypeMessage.Restrictive,
            EnumType.Permissive => EnumTypeMessage.Permissive,
            _ => EnumTypeMessage.Unspecified
        };

    private static string? EmptyToNull(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value;

    private static IReadOnlyList<string> SplitCsv(string value)
        => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static List<string> ParseJsonStringArray(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }
        catch
        {
            return [];
        }
    }
}
