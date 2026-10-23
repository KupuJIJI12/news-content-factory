using Microsoft.EntityFrameworkCore;

namespace NewsFactory.Infrastructure;

/// <summary>Creates independent contexts (e.g. for writes that must survive the caller's transaction).</summary>
public sealed class NewsFactoryDbContextFactory(DbContextOptions<NewsFactoryDbContext> options, TimeProvider time)
    : IDbContextFactory<NewsFactoryDbContext>
{
    public NewsFactoryDbContext CreateDbContext() => new(options, time);
}
