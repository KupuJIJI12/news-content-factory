using NewsFactory.Collection;
using NewsFactory.Infrastructure;

namespace NewsFactory.Api.Admin.Collection;

public sealed record CollectionState(bool Paused);

public static class CollectionEndpoints
{
    public static void MapCollectionEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/collection", async (NewsFactoryDbContext db, CancellationToken ct) =>
            new CollectionState(await new CollectionPause(db).IsPausedAsync(ct)));
        api.MapPut("/collection", async (CollectionState input, NewsFactoryDbContext db, CancellationToken ct) =>
        {
            await new CollectionPause(db).SetAsync(input.Paused, ct);
            return new CollectionState(input.Paused);
        });
    }
}
