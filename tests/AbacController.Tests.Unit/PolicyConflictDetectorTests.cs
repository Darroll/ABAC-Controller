using AbacController.Core.Domain.Policy;
using AbacController.Pdp;

namespace AbacController.Tests.Unit;

public sealed class PolicyConflictDetectorTests
{
    [Fact]
    public void DetectConflicts_FindsConflictingRules()
    {
        var existingPolicySets = new List<PolicySet>
        {
            new()
            {
                Id = "ops",
                Name = "Operations",
                Policies =
                [
                    new Policy
                    {
                        Id = "existing-permit",
                        PolicySetId = "ops",
                        Name = "Permit ENG",
                        Versions =
                        [
                            new PolicyVersion
                            {
                                PolicyId = "existing-permit",
                                VersionNumber = 1,
                                Content = "{\"rules\":[{\"id\":\"permit-eng\",\"effect\":\"Permit\",\"conditions\":[{\"path\":\"subject.department\",\"equals\":\"ENG\"}]}]}",
                                Hash = "h1",
                                IsActive = true
                            }
                        ]
                    }
                ]
            }
        };

        // Candidate that denies the same department
        var candidateContent = "{\"rules\":[{\"id\":\"deny-eng\",\"effect\":\"Deny\",\"conditions\":[{\"path\":\"subject.department\",\"equals\":\"ENG\"}]}]}";
        var conflicts = PolicyConflictDetector.DetectConflicts(candidateContent, existingPolicySets);

        Assert.Single(conflicts);
        Assert.Equal("deny-eng", conflicts[0].CandidateRuleId);
        Assert.Equal("Deny", conflicts[0].CandidateEffect);
        Assert.Equal("permit-eng", conflicts[0].ExistingRuleId);
        Assert.Equal("Permit", conflicts[0].ExistingEffect);
    }

    [Fact]
    public void DetectConflicts_NoConflictForSameEffect()
    {
        var existingPolicySets = new List<PolicySet>
        {
            new()
            {
                Id = "ops",
                Name = "Operations",
                Policies =
                [
                    new Policy
                    {
                        Id = "existing-permit",
                        PolicySetId = "ops",
                        Name = "Permit ENG",
                        Versions =
                        [
                            new PolicyVersion
                            {
                                PolicyId = "existing-permit",
                                VersionNumber = 1,
                                Content = "{\"rules\":[{\"id\":\"permit-eng\",\"effect\":\"Permit\",\"conditions\":[{\"path\":\"subject.department\",\"equals\":\"ENG\"}]}]}",
                                Hash = "h1",
                                IsActive = true
                            }
                        ]
                    }
                ]
            }
        };

        // Same effect — no conflict
        var candidateContent = "{\"rules\":[{\"id\":\"also-permit-eng\",\"effect\":\"Permit\",\"conditions\":[{\"path\":\"subject.department\",\"equals\":\"ENG\"}]}]}";
        var conflicts = PolicyConflictDetector.DetectConflicts(candidateContent, existingPolicySets);

        Assert.Empty(conflicts);
    }

    [Fact]
    public void DetectConflicts_NoConflictForDifferentPaths()
    {
        var existingPolicySets = new List<PolicySet>
        {
            new()
            {
                Id = "ops",
                Name = "Operations",
                Policies =
                [
                    new Policy
                    {
                        Id = "existing-permit",
                        PolicySetId = "ops",
                        Name = "Permit ENG",
                        Versions =
                        [
                            new PolicyVersion
                            {
                                PolicyId = "existing-permit",
                                VersionNumber = 1,
                                Content = "{\"rules\":[{\"id\":\"permit-eng\",\"effect\":\"Permit\",\"conditions\":[{\"path\":\"subject.department\",\"equals\":\"ENG\"}]}]}",
                                Hash = "h1",
                                IsActive = true
                            }
                        ]
                    }
                ]
            }
        };

        // Different value on same path — no overlap
        var candidateContent = "{\"rules\":[{\"id\":\"deny-sales\",\"effect\":\"Deny\",\"conditions\":[{\"path\":\"subject.department\",\"equals\":\"SALES\"}]}]}";
        var conflicts = PolicyConflictDetector.DetectConflicts(candidateContent, existingPolicySets);

        Assert.Empty(conflicts);
    }

    [Fact]
    public void DetectConflicts_CatchAllRuleConflicts()
    {
        var existingPolicySets = new List<PolicySet>
        {
            new()
            {
                Id = "ops",
                Name = "Operations",
                Policies =
                [
                    new Policy
                    {
                        Id = "existing-permit",
                        PolicySetId = "ops",
                        Name = "Permit All",
                        Versions =
                        [
                            new PolicyVersion
                            {
                                PolicyId = "existing-permit",
                                VersionNumber = 1,
                                Content = "{\"rules\":[{\"id\":\"permit-all\",\"effect\":\"Permit\"}]}",
                                Hash = "h1",
                                IsActive = true
                            }
                        ]
                    }
                ]
            }
        };

        // Catch-all deny conflicts with catch-all permit
        var candidateContent = "{\"rules\":[{\"id\":\"deny-all\",\"effect\":\"Deny\"}]}";
        var conflicts = PolicyConflictDetector.DetectConflicts(candidateContent, existingPolicySets);

        Assert.Single(conflicts);
    }

    [Fact]
    public void DetectConflicts_EmptyPolicySets_NoConflicts()
    {
        var candidateContent = "{\"rules\":[{\"id\":\"permit-all\",\"effect\":\"Permit\"}]}";
        var conflicts = PolicyConflictDetector.DetectConflicts(candidateContent, []);

        Assert.Empty(conflicts);
    }

    [Fact]
    public void DetectConflicts_InvalidJson_NoConflicts()
    {
        var conflicts = PolicyConflictDetector.DetectConflicts("not-json", []);
        Assert.Empty(conflicts);
    }
}
