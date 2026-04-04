using AbacController.Core.Constants;
using AbacController.Core.Domain.Decisions;
using AbacController.Core.Domain.Policy;
using AbacController.Pdp;

namespace AbacController.Tests.Unit;

/// <summary>
/// Tests for XACML 3.0 XML round-trip: request, response, policy set import/export.
/// </summary>
public sealed class XacmlXmlMapperTests
{
    // ─── Request round-trip ───

    [Fact]
    public void ImportRequest_ValidXacmlXml_ParsesCorrectly()
    {
        var xml = """
            <Request xmlns="urn:oasis:names:tc:xacml:3.0:core:schema:wd-17"
                     CombinedDecision="false" ReturnPolicyIdList="false">
                <Attributes Category="urn:oasis:names:tc:xacml:1.0:subject-category:access-subject">
                    <Attribute AttributeId="urn:oasis:names:tc:xacml:1.0:subject:subject-id" IncludeInResult="false">
                        <AttributeValue DataType="http://www.w3.org/2001/XMLSchema#string">alice</AttributeValue>
                    </Attribute>
                </Attributes>
                <Attributes Category="urn:oasis:names:tc:xacml:3.0:attribute-category:resource">
                    <Attribute AttributeId="urn:oasis:names:tc:xacml:1.0:resource:resource-id" IncludeInResult="false">
                        <AttributeValue DataType="http://www.w3.org/2001/XMLSchema#string">doc-123</AttributeValue>
                    </Attribute>
                </Attributes>
                <Attributes Category="urn:oasis:names:tc:xacml:3.0:attribute-category:action">
                    <Attribute AttributeId="urn:oasis:names:tc:xacml:1.0:action:action-id" IncludeInResult="false">
                        <AttributeValue DataType="http://www.w3.org/2001/XMLSchema#string">read</AttributeValue>
                    </Attribute>
                </Attributes>
            </Request>
            """;

        var request = XacmlXmlMapper.ImportRequest(xml);

        Assert.NotNull(request);
        Assert.Equal("alice", request.Subject.Id);
        Assert.Equal("read", request.Action.Name);
        Assert.Equal("doc-123", request.Resource.Id);
    }

    [Fact]
    public void ImportRequest_WithEnvironment_ParsesContextInfo()
    {
        var xml = """
            <Request xmlns="urn:oasis:names:tc:xacml:3.0:core:schema:wd-17"
                     CombinedDecision="false" ReturnPolicyIdList="false">
                <Attributes Category="urn:oasis:names:tc:xacml:1.0:subject-category:access-subject">
                    <Attribute AttributeId="urn:oasis:names:tc:xacml:1.0:subject:subject-id">
                        <AttributeValue DataType="http://www.w3.org/2001/XMLSchema#string">bob</AttributeValue>
                    </Attribute>
                </Attributes>
                <Attributes Category="urn:oasis:names:tc:xacml:3.0:attribute-category:resource">
                    <Attribute AttributeId="urn:oasis:names:tc:xacml:1.0:resource:resource-id">
                        <AttributeValue DataType="http://www.w3.org/2001/XMLSchema#string">res-1</AttributeValue>
                    </Attribute>
                </Attributes>
                <Attributes Category="urn:oasis:names:tc:xacml:3.0:attribute-category:action">
                    <Attribute AttributeId="urn:oasis:names:tc:xacml:1.0:action:action-id">
                        <AttributeValue DataType="http://www.w3.org/2001/XMLSchema#string">write</AttributeValue>
                    </Attribute>
                </Attributes>
                <Attributes Category="urn:oasis:names:tc:xacml:3.0:attribute-category:environment">
                    <Attribute AttributeId="urn:oasis:names:tc:xacml:1.0:environment:current-time">
                        <AttributeValue DataType="http://www.w3.org/2001/XMLSchema#string">2025-01-15T10:30:00Z</AttributeValue>
                    </Attribute>
                </Attributes>
            </Request>
            """;

        var request = XacmlXmlMapper.ImportRequest(xml);

        Assert.NotNull(request);
        Assert.Equal("bob", request.Subject.Id);
        Assert.Equal("write", request.Action.Name);
        Assert.NotNull(request.Context);
        Assert.True(request.Context.Environment.ContainsKey("current-time"));
    }

    [Fact]
    public void ImportRequest_NullOrEmpty_ReturnsNull()
    {
        Assert.Null(XacmlXmlMapper.ImportRequest((string)null!));
        Assert.Null(XacmlXmlMapper.ImportRequest(""));
        Assert.Null(XacmlXmlMapper.ImportRequest("   "));
    }

    [Fact]
    public void ImportRequest_InvalidXml_ReturnsNull()
    {
        Assert.Null(XacmlXmlMapper.ImportRequest("<not-valid-xml>"));
    }

    [Fact]
    public void ImportRequest_WrongRootElement_ReturnsNull()
    {
        var xml = """<PolicySet xmlns="urn:oasis:names:tc:xacml:3.0:core:schema:wd-17" PolicySetId="ps1" PolicyCombiningAlgId="x"><Target/></PolicySet>""";
        Assert.Null(XacmlXmlMapper.ImportRequest(xml));
    }

    [Fact]
    public void ExportRequest_RoundTrip_PreservesValues()
    {
        var original = new EvaluationRequest
        {
            Subject = new SubjectInfo { Type = "user", Id = "alice", Properties = new() { ["subject-id"] = "alice", ["clearance"] = "secret" } },
            Action = new ActionInfo { Name = "read", Properties = new() { ["action-id"] = "read" } },
            Resource = new ResourceInfo { Type = "document", Id = "doc-42", Properties = new() { ["resource-id"] = "doc-42", ["resource-type"] = "document" } },
            Context = new ContextInfo { Environment = new() { ["location"] = "office" } }
        };

        var xml = XacmlXmlMapper.ExportRequest(original);
        Assert.Contains("Request", xml);
        Assert.Contains("alice", xml);

        var roundTrip = XacmlXmlMapper.ImportRequest(xml);
        Assert.NotNull(roundTrip);
        Assert.Equal("alice", roundTrip.Subject.Id);
        Assert.Equal("read", roundTrip.Action.Name);
        Assert.Equal("doc-42", roundTrip.Resource.Id);
    }

    [Fact]
    public void ImportRequest_WithBooleanAndIntegerDataTypes_ParsesCorrectly()
    {
        var xml = """
            <Request xmlns="urn:oasis:names:tc:xacml:3.0:core:schema:wd-17">
                <Attributes Category="urn:oasis:names:tc:xacml:1.0:subject-category:access-subject">
                    <Attribute AttributeId="urn:oasis:names:tc:xacml:1.0:subject:subject-id">
                        <AttributeValue DataType="http://www.w3.org/2001/XMLSchema#string">user1</AttributeValue>
                    </Attribute>
                    <Attribute AttributeId="urn:oasis:names:tc:xacml:1.0:subject:age">
                        <AttributeValue DataType="http://www.w3.org/2001/XMLSchema#integer">30</AttributeValue>
                    </Attribute>
                    <Attribute AttributeId="urn:oasis:names:tc:xacml:1.0:subject:active">
                        <AttributeValue DataType="http://www.w3.org/2001/XMLSchema#boolean">true</AttributeValue>
                    </Attribute>
                </Attributes>
                <Attributes Category="urn:oasis:names:tc:xacml:3.0:attribute-category:resource">
                    <Attribute AttributeId="urn:oasis:names:tc:xacml:1.0:resource:resource-id">
                        <AttributeValue DataType="http://www.w3.org/2001/XMLSchema#string">res</AttributeValue>
                    </Attribute>
                </Attributes>
                <Attributes Category="urn:oasis:names:tc:xacml:3.0:attribute-category:action">
                    <Attribute AttributeId="urn:oasis:names:tc:xacml:1.0:action:action-id">
                        <AttributeValue DataType="http://www.w3.org/2001/XMLSchema#string">view</AttributeValue>
                    </Attribute>
                </Attributes>
            </Request>
            """;

        var request = XacmlXmlMapper.ImportRequest(xml);
        Assert.NotNull(request);
        Assert.Equal(30L, request.Subject.Properties["age"]);
        Assert.Equal(true, request.Subject.Properties["active"]);
    }

    // ─── Response round-trip ───

    [Fact]
    public void ExportResponse_PermitDecision_GeneratesValidXml()
    {
        var result = new EvaluationResult
        {
            DecisionId = "d1",
            Decision = Decision.Permit,
            Status = new StatusInfo { Code = "ok", Message = "Access granted" }
        };

        var xml = XacmlXmlMapper.ExportResponse(result);

        Assert.Contains("<Decision>Permit</Decision>", xml);
        Assert.Contains("urn:oasis:names:tc:xacml:1.0:status:ok", xml);
        Assert.Contains("Access granted", xml);
    }

    [Fact]
    public void ExportResponse_DenyWithObligations_IncludesObligations()
    {
        var result = new EvaluationResult
        {
            DecisionId = "d2",
            Decision = Decision.Deny,
            Obligations = [new Obligation { Id = "obl-1", Attributes = new() { ["reason"] = "too high" } }]
        };

        var xml = XacmlXmlMapper.ExportResponse(result);

        Assert.Contains("<Decision>Deny</Decision>", xml);
        Assert.Contains("obl-1", xml);
        Assert.Contains("reason", xml);
        Assert.Contains("too high", xml);
    }

    [Fact]
    public void ExportResponse_WithAdvice_IncludesAdvice()
    {
        var result = new EvaluationResult
        {
            DecisionId = "d3",
            Decision = Decision.Permit,
            Advice = [new Advice { Id = "adv-1", Attributes = new() { ["hint"] = "cache for 5m" } }]
        };

        var xml = XacmlXmlMapper.ExportResponse(result);
        Assert.Contains("adv-1", xml);
        Assert.Contains("hint", xml);
    }

    [Fact]
    public void ExportResponse_IndeterminateDecision_SetsProcessingErrorStatus()
    {
        var result = new EvaluationResult
        {
            DecisionId = "d4",
            Decision = Decision.Indeterminate,
            Status = new StatusInfo { Code = "error", Message = "Missing SPIF" }
        };

        var xml = XacmlXmlMapper.ExportResponse(result);
        Assert.Contains("<Decision>Indeterminate</Decision>", xml);
        Assert.Contains("processing-error", xml);
    }

    [Fact]
    public void ImportResponse_RoundTrip_PreservesDecision()
    {
        var original = new EvaluationResult
        {
            DecisionId = "d5",
            Decision = Decision.Permit,
            Status = new StatusInfo { Code = "ok", Message = "Allowed" },
            Obligations = [new Obligation { Id = "obl-log", Attributes = new() { ["action"] = "log" } }],
            Advice = [new Advice { Id = "adv-ttl", Attributes = new() { ["ttl"] = "300" } }]
        };

        var xml = XacmlXmlMapper.ExportResponse(original);
        var results = XacmlXmlMapper.ImportResponse(xml);

        Assert.NotNull(results);
        Assert.Single(results);
        Assert.Equal(Decision.Permit, results[0].Decision);
        Assert.Single(results[0].Obligations);
        Assert.Equal("obl-log", results[0].Obligations[0].Id);
        Assert.Single(results[0].Advice);
        Assert.Equal("adv-ttl", results[0].Advice[0].Id);
    }

    [Fact]
    public void ImportResponse_NullOrEmpty_ReturnsNull()
    {
        Assert.Null(XacmlXmlMapper.ImportResponse((string)null!));
        Assert.Null(XacmlXmlMapper.ImportResponse(""));
    }

    [Fact]
    public void ImportResponse_InvalidXml_ReturnsNull()
    {
        Assert.Null(XacmlXmlMapper.ImportResponse("<broken"));
    }

    [Fact]
    public void ExportBatchResponse_MultipleResults_AllIncluded()
    {
        var batch = new BatchEvaluationResult
        {
            BatchDecisionId = "batch-1",
            Evaluations =
            [
                new EvaluationResult { DecisionId = "b1", Decision = Decision.Permit },
                new EvaluationResult { DecisionId = "b2", Decision = Decision.Deny }
            ]
        };

        var xml = XacmlXmlMapper.ExportBatchResponse(batch);

        Assert.Contains("Permit", xml);
        Assert.Contains("Deny", xml);

        var imported = XacmlXmlMapper.ImportResponse(xml);
        Assert.NotNull(imported);
        Assert.Equal(2, imported.Count);
        Assert.Equal(Decision.Permit, imported[0].Decision);
        Assert.Equal(Decision.Deny, imported[1].Decision);
    }

    // ─── PolicySet round-trip ───

    [Fact]
    public void ExportPolicySet_BasicPolicySet_GeneratesValidXacmlXml()
    {
        var policySet = CreateTestPolicySet();

        var xml = XacmlXmlMapper.ExportPolicySet(policySet);

        Assert.Contains("PolicySet", xml);
        Assert.Contains("test-policy-set", xml);
        Assert.Contains("deny-overrides", xml);
        Assert.Contains("rule-1", xml);
    }

    [Fact]
    public void ImportPolicySet_RoundTrip_PreservesStructure()
    {
        var original = CreateTestPolicySet();
        var xml = XacmlXmlMapper.ExportPolicySet(original);

        var imported = XacmlXmlMapper.ImportPolicySet(xml);

        Assert.NotNull(imported);
        Assert.Equal("test-policy-set", imported.Id);
        Assert.Equal("deny-overrides", imported.CombiningAlgorithm);
        Assert.Single(imported.Policies);
        Assert.Equal("test-policy", imported.Policies[0].Id);
        Assert.Equal("xacml-xml", imported.Policies[0].Format);
        Assert.Single(imported.Policies[0].Versions);
        Assert.True(imported.Policies[0].Versions[0].IsActive);
    }

    [Fact]
    public void ImportPolicySet_NullOrEmpty_ReturnsNull()
    {
        Assert.Null(XacmlXmlMapper.ImportPolicySet((string)null!));
        Assert.Null(XacmlXmlMapper.ImportPolicySet(""));
    }

    [Fact]
    public void ImportPolicySet_InvalidXml_ReturnsNull()
    {
        Assert.Null(XacmlXmlMapper.ImportPolicySet("<bad-xml"));
    }

    [Fact]
    public void ImportPolicySet_WrongRootElement_ReturnsNull()
    {
        var xml = """<Request xmlns="urn:oasis:names:tc:xacml:3.0:core:schema:wd-17"/>""";
        Assert.Null(XacmlXmlMapper.ImportPolicySet(xml));
    }

    [Fact]
    public void ExportPolicySet_WithDescription_IncludesDescriptionElement()
    {
        var policySet = new PolicySet
        {
            Id = "desc-ps",
            Name = "desc-ps",
            Description = "A test policy set with description",
            CombiningAlgorithm = "permit-overrides",
            Policies = []
        };

        var xml = XacmlXmlMapper.ExportPolicySet(policySet);
        Assert.Contains("A test policy set with description", xml);
        Assert.Contains("permit-overrides", xml);
    }

    [Fact]
    public void ExportPolicySet_WithObligationsAndAdvice_IncludesExpressions()
    {
        var content = """
        {
            "combiningAlgorithm": "first-applicable",
            "rules": [
                {
                    "id": "r1",
                    "effect": "permit",
                    "conditions": [
                        { "path": "subject.id", "equals": "admin" }
                    ],
                    "obligations": [
                        { "id": "log-access", "attributes": { "level": "info" } }
                    ],
                    "advice": [
                        { "id": "cache-hint", "attributes": { "ttl": "60" } }
                    ]
                }
            ]
        }
        """;
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(content)));

        var policySet = new PolicySet
        {
            Id = "obl-ps",
            Name = "obl-ps",
            Policies =
            [
                new Policy
                {
                    Id = "obl-policy",
                    PolicySetId = "obl-ps",
                    Name = "obl-policy",
                    Versions =
                    [
                        new PolicyVersion
                        {
                            PolicyId = "obl-policy",
                            VersionNumber = 1,
                            Content = content,
                            Hash = hash,
                            IsActive = true
                        }
                    ]
                }
            ]
        };

        var xml = XacmlXmlMapper.ExportPolicySet(policySet);

        Assert.Contains("log-access", xml);
        Assert.Contains("cache-hint", xml);
        Assert.Contains("ObligationExpression", xml);
        Assert.Contains("AdviceExpression", xml);

        // Round-trip
        var imported = XacmlXmlMapper.ImportPolicySet(xml);
        Assert.NotNull(imported);
        Assert.Single(imported.Policies);
    }

    [Fact]
    public void ImportPolicySet_PermitOverrides_NormalizesAlgorithm()
    {
        var xml = """
            <PolicySet xmlns="urn:oasis:names:tc:xacml:3.0:core:schema:wd-17"
                       PolicySetId="test-ps"
                       PolicyCombiningAlgId="urn:oasis:names:tc:xacml:3.0:rule-combining-algorithm:permit-overrides">
                <Target/>
            </PolicySet>
            """;

        var imported = XacmlXmlMapper.ImportPolicySet(xml);
        Assert.NotNull(imported);
        Assert.Equal("permit-overrides", imported.CombiningAlgorithm);
    }

    [Fact]
    public void ExportPolicy_StandalonePolicy_GeneratesValidXml()
    {
        var content = """
        {
            "combiningAlgorithm": "deny-overrides",
            "rules": [
                { "id": "deny-all", "effect": "deny" }
            ]
        }
        """;
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(content)));

        var policy = new Policy
        {
            Id = "standalone",
            PolicySetId = "ps1",
            Name = "standalone",
            Versions =
            [
                new PolicyVersion { PolicyId = "standalone", VersionNumber = 1, Content = content, Hash = hash, IsActive = true }
            ]
        };

        var xml = XacmlXmlMapper.ExportPolicy(policy, "ps1");
        Assert.Contains("standalone", xml);
        Assert.Contains("deny-all", xml);
        Assert.Contains("Deny", xml);
    }

    [Fact]
    public void ImportPolicySet_WithInCondition_ParsesCorrectly()
    {
        // Export a policy with "in" condition, then re-import
        var content = """
        {
            "combiningAlgorithm": "deny-overrides",
            "rules": [
                {
                    "id": "role-check",
                    "effect": "permit",
                    "conditions": [
                        { "path": "subject.properties.role", "in": ["admin", "editor", "viewer"] }
                    ]
                }
            ]
        }
        """;
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(content)));

        var policySet = new PolicySet
        {
            Id = "in-ps",
            Name = "in-ps",
            Policies =
            [
                new Policy
                {
                    Id = "in-policy",
                    PolicySetId = "in-ps",
                    Name = "in-policy",
                    Versions =
                    [
                        new PolicyVersion { PolicyId = "in-policy", VersionNumber = 1, Content = content, Hash = hash, IsActive = true }
                    ]
                }
            ]
        };

        var xml = XacmlXmlMapper.ExportPolicySet(policySet);
        Assert.Contains("string-is-in", xml);
        Assert.Contains("admin", xml);
        Assert.Contains("editor", xml);
        Assert.Contains("viewer", xml);

        // Round-trip
        var imported = XacmlXmlMapper.ImportPolicySet(xml);
        Assert.NotNull(imported);
        Assert.Single(imported.Policies);
    }

    [Fact]
    public void ExportResponse_NotApplicableDecision_FormatsCorrectly()
    {
        var result = new EvaluationResult
        {
            DecisionId = "na-1",
            Decision = Decision.NotApplicable
        };

        var xml = XacmlXmlMapper.ExportResponse(result);
        Assert.Contains("<Decision>NotApplicable</Decision>", xml);
    }

    [Fact]
    public void ImportPolicySet_MultipleRules_AllParsed()
    {
        var xml = """
            <PolicySet xmlns="urn:oasis:names:tc:xacml:3.0:core:schema:wd-17"
                       PolicySetId="multi-rule-ps"
                       PolicyCombiningAlgId="urn:oasis:names:tc:xacml:3.0:rule-combining-algorithm:deny-overrides">
                <Target/>
                <Policy PolicyId="multi-rule-policy"
                        RuleCombiningAlgId="urn:oasis:names:tc:xacml:1.0:rule-combining-algorithm:first-applicable"
                        Version="1">
                    <Target/>
                    <Rule RuleId="admin-permit" Effect="Permit">
                        <Target>
                            <AnyOf>
                                <AllOf>
                                    <Match MatchId="urn:oasis:names:tc:xacml:1.0:function:string-equal">
                                        <AttributeValue DataType="http://www.w3.org/2001/XMLSchema#string">admin</AttributeValue>
                                        <AttributeDesignator Category="urn:oasis:names:tc:xacml:1.0:subject-category:access-subject"
                                                             AttributeId="urn:oasis:names:tc:xacml:1.0:subject:role"
                                                             DataType="http://www.w3.org/2001/XMLSchema#string"
                                                             MustBePresent="true"/>
                                    </Match>
                                </AllOf>
                            </AnyOf>
                        </Target>
                    </Rule>
                    <Rule RuleId="deny-all" Effect="Deny"/>
                </Policy>
            </PolicySet>
            """;

        var imported = XacmlXmlMapper.ImportPolicySet(xml);
        Assert.NotNull(imported);
        Assert.Equal("multi-rule-ps", imported.Id);
        Assert.Single(imported.Policies);
        Assert.Equal("first-applicable", imported.Policies[0].Versions[0].Content.Contains("first-applicable") ? "first-applicable" : "other");
    }

    // ─── Helpers ───

    private static PolicySet CreateTestPolicySet()
    {
        var content = """
        {
            "combiningAlgorithm": "deny-overrides",
            "rules": [
                {
                    "id": "rule-1",
                    "effect": "permit",
                    "conditions": [
                        { "path": "subject.id", "equals": "alice" },
                        { "path": "action.name", "equals": "read" }
                    ]
                },
                {
                    "id": "rule-2",
                    "effect": "deny"
                }
            ]
        }
        """;

        var hash = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(content)));

        return new PolicySet
        {
            Id = "test-policy-set",
            Name = "test-policy-set",
            Description = "Test policy set for XACML XML mapping",
            CombiningAlgorithm = "deny-overrides",
            Policies =
            [
                new Policy
                {
                    Id = "test-policy",
                    PolicySetId = "test-policy-set",
                    Name = "test-policy",
                    Versions =
                    [
                        new PolicyVersion
                        {
                            PolicyId = "test-policy",
                            VersionNumber = 1,
                            Content = content,
                            Hash = hash,
                            IsActive = true
                        }
                    ]
                }
            ]
        };
    }
}
