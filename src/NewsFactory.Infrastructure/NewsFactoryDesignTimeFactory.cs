using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace NewsFactory.Infrastructure;

public sealed class NewsFactoryDesignTimeFactory : IDesignTimeDbContextFactory<NewsFactoryDbContext>
{
    public NewsFactoryDbContext CreateDbContext(string[] args)
    {
        var cs = Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")
                 ?? "Host=localhost;Database=newsfactory;Username=postgres;Password=postgres";
        var options = new DbContextOptionsBuilder<NewsFactoryDbContext>().UseNpgsql(cs).Options;
        return new NewsFactoryDbContext(options, TimeProvider.System);
    }
}
