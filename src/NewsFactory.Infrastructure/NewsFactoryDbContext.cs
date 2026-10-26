using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using NewsFactory.Domain.Entities;

namespace NewsFactory.Infrastructure;

public class NewsFactoryDbContext(DbContextOptions<NewsFactoryDbContext> options, TimeProvider time) : DbContext(options)
{
    public DbSet<City> Cities => Set<City>();
    public DbSet<Source> Sources => Set<Source>();
    public DbSet<SourceCity> SourceCities => Set<SourceCity>();
    public DbSet<RawItem> RawItems => Set<RawItem>();
    public DbSet<Story> Stories => Set<Story>();
    public DbSet<Draft> Drafts => Set<Draft>();
    public DbSet<StoryImage> StoryImages => Set<StoryImage>();
    public DbSet<Channel> Channels => Set<Channel>();
    public DbSet<Publication> Publications => Set<Publication>();
    public DbSet<User> Users => Set<User>();
    public DbSet<LlmProvider> LlmProviders => Set<LlmProvider>();
    public DbSet<LlmTaskSetting> LlmTaskSettings => Set<LlmTaskSetting>();
    public DbSet<PromptTemplate> PromptTemplates => Set<PromptTemplate>();
    public DbSet<LlmCall> LlmCalls => Set<LlmCall>();
    public DbSet<AppSetting> AppSettings => Set<AppSetting>();

    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        builder.Properties<DateTimeOffset>().HaveConversion<UtcConverter>();
        builder.Properties<DateTimeOffset?>().HaveConversion<NullableUtcConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(NewsFactoryDbContext).Assembly);

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        Stamp();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        Stamp();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void Stamp()
    {
        var now = time.GetUtcNow();
        foreach (var entry in ChangeTracker.Entries<Entity>())
        {
            if (entry.State == EntityState.Added)
            {
                if (entry.Entity.CreatedAt == default) entry.Entity.CreatedAt = now;
                entry.Entity.UpdatedAt = now;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
            }
        }
    }

    private sealed class UtcConverter() : ValueConverter<DateTimeOffset, DateTimeOffset>(v => v.ToUniversalTime(), v => v);

    private sealed class NullableUtcConverter() : ValueConverter<DateTimeOffset?, DateTimeOffset?>(
        v => v.HasValue ? v.Value.ToUniversalTime() : v, v => v);
}
