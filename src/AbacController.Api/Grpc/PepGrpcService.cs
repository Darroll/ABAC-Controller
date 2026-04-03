using AbacController.Core.Interfaces;
using AbacController.Data;
using Grpc.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace AbacController.Api.Grpc;

[Authorize(Policy = "PepRead")]
public sealed class PepGrpcService : PepApi.PepApiBase
{
    private readonly AbacDbContext _dbContext;
    private readonly ILabelCodecRegistry _labelCodecRegistry;
    private readonly ISpifRegistry _spifRegistry;
    private readonly AbacController.Pep.LabelValidator _labelValidator;
    private readonly IMarkingGenerator _markingGenerator;

    public PepGrpcService(
        AbacDbContext dbContext,
        ILabelCodecRegistry labelCodecRegistry,
        ISpifRegistry spifRegistry,
        AbacController.Pep.LabelValidator labelValidator,
        IMarkingGenerator markingGenerator)
    {
        _dbContext = dbContext;
        _labelCodecRegistry = labelCodecRegistry;
        _spifRegistry = spifRegistry;
        _labelValidator = labelValidator;
        _markingGenerator = markingGenerator;
    }

    public override async Task<ListEnforcementPointsResponseMessage> ListEnforcementPoints(ListEnforcementPointsRequestMessage request, ServerCallContext context)
    {
        var entities = await _dbContext.EnforcementPoints
            .AsNoTracking()
            .OrderBy(static ep => ep.Id)
            .ToListAsync(context.CancellationToken);

        var response = new ListEnforcementPointsResponseMessage();
        response.EnforcementPoints.AddRange(entities.Select(ProtoMapper.ToProto));
        return response;
    }

    [Authorize(Policy = "PepAdmin")]
    public override async Task<EnforcementPointMessage> UpsertEnforcementPoint(UpsertEnforcementPointRequestMessage request, ServerCallContext context)
    {
        if (request.EnforcementPoint is null || string.IsNullOrWhiteSpace(request.EnforcementPoint.Id))
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "enforcement_point.id is required."));
        }

        var entity = ProtoMapper.ToEntity(request.EnforcementPoint);
        var existing = await _dbContext.EnforcementPoints.FindAsync([entity.Id], context.CancellationToken);
        if (existing is null)
        {
            _dbContext.EnforcementPoints.Add(entity);
            await _dbContext.SaveChangesAsync(context.CancellationToken);
            return ProtoMapper.ToProto(entity);
        }

        existing.Name = entity.Name;
        existing.Type = entity.Type;
        existing.Endpoint = entity.Endpoint;
        existing.EnforcementMode = entity.EnforcementMode;
        existing.PolicySetBindings = entity.PolicySetBindings;
        existing.SpifId = entity.SpifId;
        existing.UpdatedAt = DateTimeOffset.UtcNow;

        await _dbContext.SaveChangesAsync(context.CancellationToken);
        return ProtoMapper.ToProto(existing);
    }

    [Authorize(Policy = "PepAdmin")]
    public override async Task<OperationStatusMessage> DeleteEnforcementPoint(DeleteEnforcementPointRequestMessage request, ServerCallContext context)
    {
        var existing = await _dbContext.EnforcementPoints.FindAsync([request.Id], context.CancellationToken);
        if (existing is null)
        {
            return new OperationStatusMessage { Success = false, Message = $"Enforcement point '{request.Id}' was not found." };
        }

        _dbContext.EnforcementPoints.Remove(existing);
        await _dbContext.SaveChangesAsync(context.CancellationToken);
        return new OperationStatusMessage { Success = true, Message = $"Deleted enforcement point '{request.Id}'." };
    }

    [Authorize(Policy = "PepLabel")]
    public override Task<DecodeLabelResponseMessage> DecodeLabel(DecodeLabelRequestMessage request, ServerCallContext context)
    {
        var codec = ResolveCodec(request.CodecId);
        var decoded = codec.Decode(request.Content);
        if (!decoded.IsSuccess || decoded.Label is null)
        {
            return Task.FromResult(new DecodeLabelResponseMessage
            {
                Success = false,
                Error = decoded.Error ?? "Failed to decode label."
            });
        }

        return Task.FromResult(new DecodeLabelResponseMessage
        {
            Success = true,
            Label = ProtoMapper.ToProto(decoded.Label)
        });
    }

    [Authorize(Policy = "PepLabel")]
    public override Task<EncodeLabelResponseMessage> EncodeLabel(EncodeLabelRequestMessage request, ServerCallContext context)
    {
        var codec = ResolveCodec(request.CodecId);
        if (request.Label is null)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "label is required."));
        }

        var label = ProtoMapper.ToDomain(request.Label);
        var spifIndex = ResolveSpif(request.PolicyOid, label.PolicyOid);
        var encoded = codec.Encode(label, spifIndex);
        if (!encoded.IsSuccess || encoded.EncodedString is null)
        {
            return Task.FromResult(new EncodeLabelResponseMessage
            {
                Success = false,
                Error = encoded.Error ?? "Failed to encode label."
            });
        }

        return Task.FromResult(new EncodeLabelResponseMessage
        {
            Success = true,
            Content = encoded.EncodedString,
            ContentType = codec.ContentType,
            Marking = _markingGenerator.GenerateMarking(label, spifIndex)
        });
    }

    [Authorize(Policy = "PepLabel")]
    public override Task<ValidateLabelResponseMessage> ValidateLabel(ValidateLabelRequestMessage request, ServerCallContext context)
    {
        if (request.Label is null)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "label is required."));
        }

        var label = ProtoMapper.ToDomain(request.Label);
        var spifIndex = ResolveSpif(request.PolicyOid, label.PolicyOid);
        var validation = _labelValidator.Validate(label, spifIndex);

        return Task.FromResult(new ValidateLabelResponseMessage
        {
            IsValid = validation.IsValid,
            Marking = validation.IsValid ? _markingGenerator.GenerateMarking(label, spifIndex) : string.Empty,
            Errors = { validation.Errors }
        });
    }

    private ILabelCodec ResolveCodec(string? codecId)
    {
        var effectiveCodecId = string.IsNullOrWhiteSpace(codecId) ? "stanag4774-xml" : codecId;
        try
        {
            return _labelCodecRegistry.GetCodec(effectiveCodecId);
        }
        catch (InvalidOperationException ex)
        {
            throw new RpcException(new Status(StatusCode.NotFound, ex.Message));
        }
    }

    private ISpifIndex ResolveSpif(string? explicitPolicyOid, string? labelPolicyOid)
    {
        if (!string.IsNullOrWhiteSpace(explicitPolicyOid))
        {
            var fromExplicit = _spifRegistry.GetByPolicyOid(explicitPolicyOid);
            if (fromExplicit is not null)
            {
                return fromExplicit;
            }
        }

        if (!string.IsNullOrWhiteSpace(labelPolicyOid))
        {
            var fromLabel = _spifRegistry.GetByPolicyOid(labelPolicyOid);
            if (fromLabel is not null)
            {
                return fromLabel;
            }
        }

        return _spifRegistry.GetDefault()
            ?? throw new RpcException(new Status(StatusCode.FailedPrecondition, "No SPIF is currently registered."));
    }
}
