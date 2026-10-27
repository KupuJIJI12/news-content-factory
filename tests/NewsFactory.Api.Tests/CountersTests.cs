using System.Net;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NewsFactory.Api.Admin.Counters;
using NewsFactory.Domain;
using NewsFactory.Domain.Entities;
using NewsFactory.Infrastructure;
using NewsFactory.TestSupport;

namespace NewsFactory.Api.Tests;

internal sealed class FakeHubContext : IHubContext<AdminHub>
{
    private readonly FakeClients _clients = new();
    public List<(string Method, object?[] Args)> Sent => _clients.Proxy.Sent;
    public IHubClients Clients => _clients;
    public IGroupManager Groups => throw new NotSupportedException();

    private sealed class FakeClients : IHubClients
    {
        public FakeProxy Proxy { get; } = new();
        public IClientProxy All => Proxy;
        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => Proxy;
        public IClientProxy Client(string connectionId) => Proxy;
        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => Proxy;
        public IClientProxy Group(string groupName) => Proxy;
        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => Proxy;
        public IClientProxy Groups(IReadOnlyList<string> groupNames) => Proxy;
        public IClientProxy User(string userId) => Proxy;
        public IClientProxy Users(IReadOnlyList<string> userIds) => Proxy;
    }

    internal sealed class FakeProxy : IClientProxy
    {
        public List<(string Method, object?[] Args)> Sent { get; } = [];
        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
        {
            Sent.Add((method, args));
            return Task.CompletedTask;
        }
    }
}

[Collection("db")]
public class CountersTests(PostgresFixture db)
{
    private static Story StoryWith(City city, StoryStatus status, bool hasUpdates = false)
    {
        var s = TestData.Story(city, status);
        s.HasUpdates = hasUpdates;
        s.Drafts.Add(new Draft { Version = 1, Title = "t", Body = "b", IsCurrent = true });
        return s;
    }

    [Fact]
    public async Task Counts_review_updates_errors_and_today()
    {
        await db.ResetAsync();
        await using var host = await AdminTestHost.StartAsync(db);
        var now = DateTimeOffset.UtcNow;
        await host.WithDbAsync(async d =>
        {
            var city = await d.Cities.SingleAsync();
            var channel = await d.Channels.SingleAsync();
            d.AddRange(StoryWith(city, StoryStatus.ReadyForReview), StoryWith(city, StoryStatus.ReadyForReview, hasUpdates: true),
                StoryWith(city, StoryStatus.LowInterest, hasUpdates: true), StoryWith(city, StoryStatus.ProcessingFailed),
                StoryWith(city, StoryStatus.PublishFailed));
            var failing = TestData.RssSource(); failing.ConsecutiveFailures = 3;
            var silent = TestData.RssSource(); silent.ExpectedFrequencyHours = 24; silent.LastItemAt = now.AddHours(-30);
            var disabled = TestData.RssSource(); disabled.ConsecutiveFailures = 5; disabled.IsEnabled = false;
            d.AddRange(failing, silent, disabled);
            var r1 = TestData.RawItem(failing); r1.Status = RawItemStatus.Failed; r1.CollectedAt = now.AddHours(-20);
            var r2 = TestData.RawItem(failing); r2.Status = RawItemStatus.Failed; r2.CollectedAt = now.AddDays(-3);
            d.AddRange(r1, r2);
            await d.SaveChangesAsync();
            var pubStory = StoryWith(city, StoryStatus.Published);
            d.Add(pubStory);
            await d.SaveChangesAsync();
            var draftId = pubStory.Drafts.Single().Id;
            d.AddRange(
                new Publication { StoryId = pubStory.Id, DraftId = draftId, ChannelId = channel.Id, Status = PublicationStatus.Published, ScheduledAt = now, PublishedAt = now },
                new Publication { StoryId = pubStory.Id, DraftId = draftId, ChannelId = channel.Id, Status = PublicationStatus.Cancelled, ScheduledAt = now });
            var scheduledStory = StoryWith(city, StoryStatus.Scheduled);
            d.Add(scheduledStory);
            await d.SaveChangesAsync();
            d.Add(new Publication { StoryId = scheduledStory.Id, DraftId = scheduledStory.Drafts.Single().Id, ChannelId = channel.Id,
                Status = PublicationStatus.Scheduled, ScheduledAt = now.AddMinutes(5) });
            await d.SaveChangesAsync();
        });

        var c = await host.GetJsonAsync<Counters>("/api/counters");
        Assert.Equal((2, 2), (c.OnReview, c.WithUpdates));
        Assert.Equal((1, 1, 1, 1, 1, false), (c.Errors.SourcesFailing, c.Errors.SourcesSilent, c.Errors.StoriesProcessingFailed,
            c.Errors.StoriesPublishFailed, c.Errors.RawItemsFailed24h, c.Errors.BudgetExceeded));
        Assert.Equal(5, c.Errors.Total);
        var today = c.Today.Single();
        Assert.Equal((1, 1, 4), (today.Published, today.Scheduled, today.Limit));
    }

    [Fact]
    public async Task Budget_spent_today_is_reported()
    {
        await db.ResetAsync();
        await using var host = await AdminTestHost.StartAsync(db);
        await host.WithDbAsync(async d =>
        {
            (await d.AppSettings.SingleAsync(s => s.Key == "DailyTokenBudget")).Value = "100";
            d.Add(new LlmCall { Task = LlmTask.Rewrite, Model = "m", Request = "[]", PromptTokens = 100, CompletionTokens = 50, Success = true });
            await d.SaveChangesAsync();
        });
        var c = await host.GetJsonAsync<Counters>("/api/counters");
        Assert.True(c.Errors.BudgetExceeded);
        Assert.True(c.Errors.Total >= 1);
    }

    [Fact]
    public async Task Broadcaster_sends_only_when_counters_change()
    {
        await db.ResetAsync();
        await using var host = await AdminTestHost.StartAsync(db);
        var hub = new FakeHubContext();
        var broadcaster = new CountersBroadcaster(host.Services.GetRequiredService<IServiceScopeFactory>(), hub,
            TimeProvider.System, Microsoft.Extensions.Options.Options.Create(new CountersOptions()), NullLogger<CountersBroadcaster>.Instance);

        await broadcaster.TickAsync(default);
        await broadcaster.TickAsync(default);
        Assert.Single(hub.Sent);
        Assert.Equal("CountersChanged", hub.Sent[0].Method);

        await host.WithDbAsync(async d =>
        {
            d.Add(StoryWith(await d.Cities.SingleAsync(), StoryStatus.ReadyForReview));
            await d.SaveChangesAsync();
        });
        await broadcaster.TickAsync(default);
        Assert.Equal(2, hub.Sent.Count);
        Assert.Equal(1, ((Counters)hub.Sent[1].Args[0]!).OnReview);
    }

    [Fact]
    public async Task Hub_rejects_anonymous_connection()
    {
        await db.ResetAsync();
        await using var host = await AdminTestHost.StartAsync(db);
        var resp = await host.Anonymous.PostAsync("/hubs/admin/negotiate?negotiateVersion=1", null);
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.Client.PostAsync("/hubs/admin/negotiate?negotiateVersion=1", null)).StatusCode);
    }
}
