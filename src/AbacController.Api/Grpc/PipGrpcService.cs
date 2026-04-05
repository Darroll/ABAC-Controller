using AbacController.Core.Domain.Attributes;
using AbacController.Core.Interfaces;
using AbacController.Data;
using Grpc.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace AbacController.Api.Grpc;

/// <summary>
/// Implements the PIP gRPC surface for source administration and attribute resolution.
/// </summary>
[Authorize(Policy = "PipRead")]
public sealed class PipGrpcService : PipApi.PipApiBase
{
    private readonly AbacDbContext _dbContext;
    private readonly IPipResolver _pipResolver;

    /// <summary>Initializes a new instance of the <see cref="PipGrpcService"/> class.</summary>
    public PipGrpcService(AbacDbContext dbContext, IPipResolver pipResolver)
    {
        _dbContext = dbContext;
        _pipResolver = pipResolver;
    }

    /// <summary>Lists configured PIP sources.</summary>
    public override async Task<ListSourcesResponseMessage> ListSources(ListSourcesRequestMessage request, ServerCallContext context)
    {
        var sources = await _dbContext.PipSources
            .AsNoTracking()
            .OrderBy(static source => source.Id)
            .ToListAsync(context.CancellationToken);

        var response = new ListSourcesResponseMessage();
        response.Sources.AddRange(sources.Select(ProtoMapper.ToProto));
        return response;
    }

    [Authorize(Policy = "PipAdmin")]
    /// <summary>Creates a PIP source or updates an existing one.</summary>
    public override async Task<PipSourceMessage> UpsertSource(UpsertSourceRequestMessage request, ServerCallContext context)
    {
        if (request.Source is null || string.IsNullOrWhiteSpace(request.Source.Id))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "source.id is required."));
        }

        var entity = ProtoMapper.ToEntity(request.Source);
        var existing = await _dbContext.PipSources.FindAsync([entity.Id], context.CancellationToken);
        if (existing is null)
        {
            _dbContext.PipSources.Add(entity);
            await _dbContext.SaveChangesAsync(context.CancellationToken);
            return ProtoMapper.ToProto(entity);
        }

        existing.Name = entity.Name;
        existing.SourceType = entity.SourceType;
        existing.ConfigJson = entity.ConfigJson;
        existing.ProvidesAttributes = entity.ProvidesAttributes;
        existing.Priority = entity.Priority;
        existing.IsRequired = entity.IsRequired;
        existing.CacheEnabled = entity.CacheEnabled;
        existing.CacheTtlSeconds = entity.CacheTtlSeconds;
        existing.CacheMaxEntries = entity.CacheMaxEntries;
        existing.UpdatedAt = DateTimeOffset.UtcNow;

        await _dbContext.SaveChangesAsync(context.CancellationToken);
        return ProtoMapper.ToProto(existing);
    }

    [Authorize(Policy = "PipAdmin")]
    /// <summary>Deletes a configured PIP source.</summary>
    public override async Task<OperationStatusMessage> DeleteSource(DeleteSourceRequestMessage request, ServerCallContext context)
    {
        var existing = await _dbContext.PipSources.FindAsync([request.Id], context.CancellationToken);
        if (existing is null)
        {
            return new OperationStatusMessage { Success = false, Message = $"PIP source '{request.Id}' was not found." };
        }

        _dbContext.PipSources.Remove(existing);
        await _dbContext.SaveChangesAsync(context.CancellationToken);
        return new OperationStatusMessage { Success = true, Message = $"Deleted PIP source '{request.Id}'." };
    }

    /// <summary>Resolves attributes for the supplied subject context.</summary>
    public override async Task<ResolveAttributesResponseMessage> Resolve(ResolveAttributesRequestMessage request, ServerCallContext context)
    {
        var result = await _pipResolver.ResolveAsync(new AttributeResolutionRequest
        {
            SubjectId = request.SubjectId,
            SubjectType = request.SubjectType,
            RequestedAttributes = request.RequestedAttributes.ToList(),
            Context = ProtoMapper.ToDictionary(request.Context)
        }, context.CancellationToken);

        return ProtoMapper.ToProto(result);
    }
}
