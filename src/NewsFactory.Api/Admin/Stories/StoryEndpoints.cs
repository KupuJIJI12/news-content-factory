namespace NewsFactory.Api.Admin.Stories;

public static class StoryEndpoints
{
    public static void MapStoryEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/stories", (StoryQueries q, QueueTab tab, int? skip, int? take, CancellationToken ct) =>
            q.ListAsync(tab, skip ?? 0, take ?? 50, ct));
        api.MapGet("/stories/{id:long}", async (long id, StoryQueries q, CancellationToken ct) =>
            await q.GetAsync(id, ct) is { } detail ? Results.Ok(detail) : Problems.NotFound("Сюжет"));
    }
}
