using System.Globalization;
using AbacController.Api.Runtime;
using AbacController.Core.Domain.Audit;
using AbacController.Core.Interfaces;
using Grpc.Core;
using Microsoft.AspNetCore.Authorization;

namespace AbacController.Api.Grpc;

/// <summary>
/// gRPC service implementation for the System API.
/// </summary>
[Authorize(Policy = "SysRead")]
public sealed class SystemGrpcService : SystemApi.SystemApiBase
{
    private readonly AppRuntimeState _runtimeState;
    private readonly ILabelCodecRegistry _labelCodecRegistry;
    private readonly ISpifRegistry _spifRegistry;
    private readonly IAuditReader _auditReader;

    public SystemGrpcService(
        AppRuntimeState runtimeState,
        ILabelCodecRegistry labelCodecRegistry,
        ISpifRegistry spifRegistry,
        IAuditReader auditReader)
    {
        _runtimeState = runtimeState;
        _labelCodecRegistry = labelCodecRegistry;
        _spifRegistry = spifRegistry;
        _auditReader = auditReader;
    }

    public override Task<GetStatusResponseMessage> GetStatus(GetStatusRequestMessage request, ServerCallContext context)
    {
        var response = new GetStatusResponseMessage
        {
            StartupCompleted = _runtimeState.StartupCompleted,
            Ready = _runtimeState.StartupCompleted,
            Version = typeof(SystemGrpcService).Assembly.GetName().Version?.ToString() ?? "0.0.0"
        };
        response.RegisteredLabelCodecs.AddRange(_labelCodecRegistry.GetRegisteredCodecIds());
        return Task.FromResult(response);
    }

    public override Task<ListRegisteredSpifsResponseMessage> ListRegisteredSpifs(ListRegisteredSpifsRequestMessage request, ServerCallContext context)
    {
        var response = new ListRegisteredSpifsResponseMessage();
        response.PolicyOids.AddRange(_spifRegistry.GetRegisteredPolicyOids());
        return Task.FromResult(response);
    }

    [Authorize(Policy = "AuditRead")]
    public override async Task<QueryAuditResponseMessage> QueryAudit(QueryAuditRequestMessage request, ServerCallContext context)
    {
        var query = new AuditQuery
        {
            From = ParseDateTime(request.From),
            To = ParseDateTime(request.To),
            SubjectId = NullIfWhiteSpace(request.SubjectId),
            ResourceId = NullIfWhiteSpace(request.ResourceId),
            EventType = NullIfWhiteSpace(request.EventType),
            Decision = NullIfWhiteSpace(request.Decision),
            Page = request.Page <= 0 ? 1 : request.Page,
            PageSize = request.PageSize <= 0 ? 50 : Math.Min(request.PageSize, 500)
        };

        var result = await _auditReader.QueryAsync(query, context.CancellationToken);
        var response = new QueryAuditResponseMessage
        {
            TotalCount = result.TotalCount,
            Page = result.Page,
            PageSize = result.PageSize,
            TotalPages = result.TotalPages
        };
        response.Events.AddRange(result.Events.Select(ProtoMapper.ToProto));
        return response;
    }

    private static DateTimeOffset? ParseDateTime(string value)
        => DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : null;

    private static string? NullIfWhiteSpace(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value;
}
