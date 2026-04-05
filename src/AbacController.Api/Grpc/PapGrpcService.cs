using AbacController.Core.Domain.Policy;
using AbacController.Core.Interfaces;
using AbacController.Data;
using Grpc.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace AbacController.Api.Grpc;

/// <summary>
/// Implements the PAP gRPC surface for policy-set queries, updates, version listings, and SPIF listings.
/// </summary>
[Authorize(Policy = "PolicyRead")]
public sealed class PapGrpcService : PapApi.PapApiBase
{
    private readonly IPolicyRepository _policyRepository;
    private readonly AbacDbContext _dbContext;

    public PapGrpcService(IPolicyRepository policyRepository, AbacDbContext dbContext)
    {
        _policyRepository = policyRepository;
        _dbContext = dbContext;
    }

    /// <summary>Lists policy sets available to the caller.</summary>
    public override async Task<ListPolicySetsResponseMessage> ListPolicySets(ListPolicySetsRequestMessage request, ServerCallContext context)
    {
        var policySets = await _policyRepository.GetPolicySetsAsync(context.CancellationToken);
        var response = new ListPolicySetsResponseMessage();
        response.PolicySets.AddRange(policySets.Select(ProtoMapper.ToProto));
        return response;
    }

    /// <summary>Gets a policy set by identifier, including active policy versions.</summary>
    public override async Task<GetPolicySetResponseMessage> GetPolicySet(GetPolicySetRequestMessage request, ServerCallContext context)
    {
        var policySet = await _policyRepository.GetPolicySetAsync(request.Id, context.CancellationToken);
        if (policySet is null)
        {
            throw new RpcException(new Status(StatusCode.NotFound, $"Policy set '{request.Id}' was not found."));
        }

        var response = new GetPolicySetResponseMessage
        {
            PolicySet = ProtoMapper.ToProto(policySet)
        };

        response.ActiveVersions.AddRange(policySet.Policies
            .SelectMany(static policy => policy.Versions)
            .Where(static version => version.IsActive)
            .OrderByDescending(static version => version.CreatedAt)
            .Select(ProtoMapper.ToProto));

        return response;
    }

    [Authorize(Policy = "PolicyWrite")]
    /// <summary>Creates a policy set or updates an existing one.</summary>
    public override async Task<PolicySetSummaryMessage> UpsertPolicySet(UpsertPolicySetRequestMessage request, ServerCallContext context)
    {
        if (request.PolicySet is null || string.IsNullOrWhiteSpace(request.PolicySet.Id))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "policy_set.id is required."));
        }

        var policySet = ProtoMapper.ToDomain(request.PolicySet);
        var existing = await _policyRepository.GetPolicySetAsync(policySet.Id, context.CancellationToken);
        var saved = existing is null
            ? await _policyRepository.CreatePolicySetAsync(policySet, context.CancellationToken)
            : await _policyRepository.UpdatePolicySetAsync(policySet, context.CancellationToken);

        return ProtoMapper.ToProto(saved);
    }

    /// <summary>Lists versions for the requested policy.</summary>
    public override async Task<ListPolicyVersionsResponseMessage> ListPolicyVersions(ListPolicyVersionsRequestMessage request, ServerCallContext context)
    {
        var versions = await _policyRepository.GetVersionsAsync(request.PolicyId, context.CancellationToken);
        var response = new ListPolicyVersionsResponseMessage();
        response.Versions.AddRange(versions.Select(ProtoMapper.ToProto));
        return response;
    }

    /// <summary>Lists registered SPIF records.</summary>
    public override async Task<ListSpifsResponseMessage> ListSpifs(ListSpifsRequestMessage request, ServerCallContext context)
    {
        var entities = await _dbContext.Spifs
            .AsNoTracking()
            .OrderBy(static spif => spif.PolicyOid)
            .ToListAsync(context.CancellationToken);

        var response = new ListSpifsResponseMessage();
        response.Spifs.AddRange(entities.Select(ProtoMapper.ToProto));
        return response;
    }
}
