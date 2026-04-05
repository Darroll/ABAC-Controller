using AbacController.Core.Constants;
using AbacController.Core.Domain.Decisions;
using AbacController.Core.Domain.Policy;
using AbacController.Pdp;

namespace AbacController.Tests.Unit;

/// <summary>
/// Extra round-trip and edge-case coverage for XACML XML mapping.
/// </summary>
public sealed class XacmlXmlMapperEdgeCaseTests
{
    [Fact]
    public void ExportBatchResponse_WithMixedDecisions_ProducesMultipleResults()
    {
        var batch = new BatchEvaluationResult
        {
            BatchDecisionId = "batch-1",
            Evaluations =
            [
                new EvaluationResult { DecisionId = "d1", Decision = Decision.Permit },
                new EvaluationResult { DecisionId = "d2", Decision = Decision.Deny },
                new EvaluationResult { DecisionId = "d3", Decision = Decision.NotApplicable }
            ]
        };

        var xml = XacmlXmlMapper.ExportBatchResponse(batch);

        Assert.Equal(3, CountOccurrences(xml, "<Result>"));
        Assert.Contains("<Decision>Permit</Decision>", xml);
        Assert.Contains("<Decision>Deny</Decision>", xml);
        Assert.Contains("<Decision>NotApplicable</Decision>", xml);
    }

    [Fact]
    public void ImportPolicySet_NormalizesCombiningAlgorithmNames()
    {
        var xml = """
            <PolicySet xmlns="urn:oasis:names:tc:xacml:3.0:core:schema:wd-17"
                       PolicySetId="ps1"
                       PolicyCombiningAlgId="urn:oasis:names:tc:xacml:1.0:policy-combining-algorithm:permit-overrides">
              <Target />
            </PolicySet>
            """;

        var policySet = XacmlXmlMapper.ImportPolicySet(xml);

        Assert.NotNull(policySet);
        Assert.Equal("permit-overrides", policySet.CombiningAlgorithm);
    }

    [Fact]
    public void ExportPolicySet_ThenImportPolicySet_PreservesPolicyIdsAndVersions()
    {
        var policySet = new PolicySet
        {
            Id = "ps-roundtrip",
            Name = "Round Trip",
            Description = "round-trip test",
            CombiningAlgorithm = "deny-overrides",
            Policies =
            [
                new Policy
                {
                    Id = "policy-1",
                    PolicySetId = "ps-roundtrip",
                    Name = "Policy One",
                    Format = "native",
                    Versions =
                    [
                        new PolicyVersion
                        {
                            Id = Guid.NewGuid(),
                            PolicyId = "policy-1",
                            VersionNumber = 1,
                            Content = "{\"id\":\"rule-1\",\"effect\":\"Permit\",\"conditions\":[]}",
                            IsActive = true,
                            CreatedAt = DateTimeOffset.UtcNow,
                            Hash = "hash-1"
                        }
                    ]
                }
            ]
        };

        var xml = XacmlXmlMapper.ExportPolicySet(policySet);
        var imported = XacmlXmlMapper.ImportPolicySet(xml);

        Assert.NotNull(imported);
        Assert.Equal("ps-roundtrip", imported.Id);
        Assert.Single(imported.Policies);
        Assert.Equal("policy-1", imported.Policies[0].Id);
        Assert.Single(imported.Policies[0].Versions);
        Assert.True(imported.Policies[0].Versions[0].IsActive);
    }

    [Fact]
    public void ImportResponse_WithUnknownDecision_FallsBackToIndeterminate()
    {
        var xml = """
            <Response xmlns="urn:oasis:names:tc:xacml:3.0:core:schema:wd-17">
              <Result>
                <Decision>SomethingUnexpected</Decision>
              </Result>
            </Response>
            """;

        var results = XacmlXmlMapper.ImportResponse(xml);

        Assert.NotNull(results);
        Assert.Single(results);
        Assert.Equal(Decision.Indeterminate, results[0].Decision);
    }

    private static int CountOccurrences(string value, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = value.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }
}
