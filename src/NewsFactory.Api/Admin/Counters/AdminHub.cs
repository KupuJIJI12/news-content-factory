using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace NewsFactory.Api.Admin.Counters;

/// <summary>Server → client only: "CountersChanged" with <see cref="Counters"/>.</summary>
[Authorize]
public sealed class AdminHub : Hub
{
    public const string Path = "/hubs/admin";
    public const string CountersChanged = "CountersChanged";
}
