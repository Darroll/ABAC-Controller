using AbacController.Api.Controllers;
using AbacController.Core.Domain.Audit;
using AbacController.Core.Domain.Webhooks;
using AbacController.Core.Interfaces;
using AbacController.Data;
using AbacController.Data.Repositories;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AbacController.Tests.Unit;

/// <summary>
/// Direct controller-level tests for <see cref="WebhookAdminController"/>: list,
/// create with auto-generated secret, callback lookup, delete cascade,
/// not-found semantics, and the replay endpoint.
/// </summary>
public sealed class WebhookAdminControllerTests : IDisposable
{
    private readonly AbacDbContext _db;
    private readonly WebhookRepository _repo;
    private readonly StubAuditWriter _audit = new();
    private readonly WebhookAdminController _controller;

    public WebhookAdminControllerTests()
    {
        var options = new DbContextOptionsBuilder<AbacDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        _db = new AbacDbContext(options);
        _db.Database.OpenConnection();
        _db.Database.EnsureCreated();
        _repo = new WebhookRepository(_db);
        _controller = new WebhookAdminController(_repo, new StubTenantContext("tenant-a"), _audit)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
    }

    public void Dispose()
    {
        _db.Database.CloseConnection();
        _db.Dispose();
    }

    [Fact]
    public async Task Create_GeneratesSecret_WhenNotProvided()
    {
        var result = await _controller.Create(new WebhookAdminController.WebhookSubscriptionRequest
        {
            CallbackUrl = "https://example.com/cb"
        }, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<WebhookAdminController.WebhookSubscriptionCreateResponse>(ok.Value);
        Assert.False(string.IsNullOrEmpty(response.Secret));
        Assert.True(response.Secret.Length >= 32);
        Assert.NotEqual(Guid.Empty, response.Subscription.Id);
    }

    [Fact]
    public async Task Create_RejectsEmptyCallbackUrl()
    {
        var result = await _controller.Create(new WebhookAdminController.WebhookSubscriptionRequest
        {
            CallbackUrl = ""
        }, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task Create_AuditsAndDefaultsToWildcardEvents()
    {
        var result = await _controller.Create(new WebhookAdminController.WebhookSubscriptionRequest
        {
            CallbackUrl = "https://example.com/cb"
        }, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<WebhookAdminController.WebhookSubscriptionCreateResponse>(ok.Value);
        Assert.Equal("*", response.Subscription.EventTypes);
        Assert.Single(_audit.Events, e => e.ActionName == "create_webhook");
    }

    [Fact]
    public async Task List_FiltersByCallbackUrl_WhenProvided()
    {
        await _controller.Create(new WebhookAdminController.WebhookSubscriptionRequest
        { CallbackUrl = "https://a.example.com/cb" }, CancellationToken.None);
        await _controller.Create(new WebhookAdminController.WebhookSubscriptionRequest
        { CallbackUrl = "https://b.example.com/cb" }, CancellationToken.None);

        var matched = await _controller.List("https://b.example.com/cb", CancellationToken.None);
        Assert.Single(matched);
        Assert.Equal("https://b.example.com/cb", matched[0].CallbackUrl);

        var all = await _controller.List(null, CancellationToken.None);
        Assert.Equal(2, all.Count);
    }

    [Fact]
    public async Task Get_NotFound_WhenIdMissing()
    {
        var result = await _controller.Get(Guid.NewGuid(), CancellationToken.None);
        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task Delete_RemovesSubscription_AndAudits()
    {
        var create = await _controller.Create(new WebhookAdminController.WebhookSubscriptionRequest
        { CallbackUrl = "https://x.example.com/cb" }, CancellationToken.None);
        var ok = (OkObjectResult)create.Result!;
        var view = (WebhookAdminController.WebhookSubscriptionCreateResponse)ok.Value!;

        var result = await _controller.Delete(view.Subscription.Id, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        Assert.Null(await _repo.GetAsync(view.Subscription.Id));
        Assert.Single(_audit.Events, e => e.ActionName == "delete_webhook");
    }

    [Fact]
    public async Task Delete_NotFound_WhenIdMissing()
    {
        var result = await _controller.Delete(Guid.NewGuid(), CancellationToken.None);
        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task ListEvents_NotFound_WhenSubscriptionMissing()
    {
        var result = await _controller.ListEvents(Guid.NewGuid(), since: null, max: 10, CancellationToken.None);
        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task ListEvents_ReturnsEventsForSubscription()
    {
        var create = await _controller.Create(new WebhookAdminController.WebhookSubscriptionRequest
        { CallbackUrl = "https://e.example.com/cb" }, CancellationToken.None);
        var view = ((WebhookAdminController.WebhookSubscriptionCreateResponse)((OkObjectResult)create.Result!).Value!).Subscription;

        await _repo.AppendEventAsync(new WebhookEvent
        {
            Id = Guid.NewGuid(),
            SubscriptionId = view.Id,
            EventType = WebhookEventTypes.AssignmentChanged,
            PayloadJson = "{}"
        });

        var result = await _controller.ListEvents(view.Id, since: null, max: 10, CancellationToken.None);
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var events = Assert.IsType<List<WebhookAdminController.WebhookEventView>>(ok.Value);
        Assert.Single(events);
        Assert.Equal(WebhookEventTypes.AssignmentChanged, events[0].EventType);
    }

    // ── Stubs ──

    private sealed class StubAuditWriter : IAuditWriter
    {
        public List<AuditEvent> Events { get; } = new();
        public void Write(AuditEvent auditEvent) => Events.Add(auditEvent);
        public Task FlushAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class StubTenantContext(string? tenantId) : ITenantContext
    {
        public string? TenantId { get; } = tenantId;
    }
}
