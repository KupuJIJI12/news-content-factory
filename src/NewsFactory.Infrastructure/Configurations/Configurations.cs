using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NewsFactory.Domain;
using NewsFactory.Domain.Entities;

namespace NewsFactory.Infrastructure.Configurations;

internal sealed class CityConfiguration : IEntityTypeConfiguration<City>
{
    public void Configure(EntityTypeBuilder<City> b) => b.HasIndex(x => x.Slug).IsUnique();
}

internal sealed class SourceConfiguration : IEntityTypeConfiguration<Source>
{
    public void Configure(EntityTypeBuilder<Source> b)
    {
        b.Property(x => x.Type).HasConversion<string>();
        b.Property(x => x.Config).HasColumnType("jsonb");
        b.HasIndex(x => x.Url).IsUnique();
    }
}

internal sealed class SourceCityConfiguration : IEntityTypeConfiguration<SourceCity>
{
    public void Configure(EntityTypeBuilder<SourceCity> b)
    {
        b.HasKey(x => new { x.SourceId, x.CityId });
        b.HasOne(x => x.Source).WithMany(s => s.Cities).HasForeignKey(x => x.SourceId);
        b.HasOne(x => x.City).WithMany().HasForeignKey(x => x.CityId);
    }
}

internal sealed class RawItemConfiguration : IEntityTypeConfiguration<RawItem>
{
    public void Configure(EntityTypeBuilder<RawItem> b)
    {
        b.Property(x => x.Status).HasConversion<string>();
        b.HasIndex(x => new { x.SourceId, x.ExternalId }).IsUnique();
        b.HasOne(x => x.Story).WithMany(s => s.RawItems).HasForeignKey(x => x.StoryId);
        b.HasOne<City>().WithMany().HasForeignKey(x => x.CityId);
    }
}

internal sealed class StoryConfiguration : IEntityTypeConfiguration<Story>
{
    public void Configure(EntityTypeBuilder<Story> b)
    {
        b.Property(x => x.Status).HasConversion<string>();
        b.HasIndex(x => new { x.CityId, x.CreatedAt });
    }
}

internal sealed class DraftConfiguration : IEntityTypeConfiguration<Draft>
{
    public void Configure(EntityTypeBuilder<Draft> b)
    {
        b.Property(x => x.FactCheckIssues).HasJsonConversion();
        b.HasOne(x => x.Story).WithMany(s => s.Drafts).HasForeignKey(x => x.StoryId);
        b.HasIndex(x => new { x.StoryId, x.Version }).IsUnique();
    }
}

internal sealed class StoryImageConfiguration : IEntityTypeConfiguration<StoryImage>
{
    public void Configure(EntityTypeBuilder<StoryImage> b) =>
        b.HasOne(x => x.Story).WithMany(s => s.Images).HasForeignKey(x => x.StoryId);
}

internal sealed class ChannelConfiguration : IEntityTypeConfiguration<Channel>
{
    public void Configure(EntityTypeBuilder<Channel> b)
    {
        b.Property(x => x.Platform).HasConversion<string>();
        b.HasIndex(x => new { x.CityId, x.Platform }).IsUnique();
    }
}

internal sealed class PublicationConfiguration : IEntityTypeConfiguration<Publication>
{
    public void Configure(EntityTypeBuilder<Publication> b)
    {
        b.Property(x => x.Status).HasConversion<string>();
        b.HasIndex(x => new { x.ChannelId, x.Status });
    }
}

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> b) => b.HasIndex(x => x.Login).IsUnique();
}

internal sealed class LlmProviderConfiguration : IEntityTypeConfiguration<LlmProvider>
{
    public void Configure(EntityTypeBuilder<LlmProvider> b)
    {
        b.Property(x => x.Kind).HasConversion<string>();
        b.Property(x => x.Headers).HasJsonConversion();
        b.HasIndex(x => x.Name).IsUnique();
    }
}

internal sealed class LlmTaskSettingConfiguration : IEntityTypeConfiguration<LlmTaskSetting>
{
    public void Configure(EntityTypeBuilder<LlmTaskSetting> b)
    {
        b.Property(x => x.Task).HasConversion<string>();
        b.HasIndex(x => x.Task).IsUnique();
    }
}

internal sealed class PromptTemplateConfiguration : IEntityTypeConfiguration<PromptTemplate>
{
    public void Configure(EntityTypeBuilder<PromptTemplate> b)
    {
        b.Property(x => x.Task).HasConversion<string>();
        b.HasIndex(x => new { x.Task, x.Version }).IsUnique();
    }
}

internal sealed class LlmCallConfiguration : IEntityTypeConfiguration<LlmCall>
{
    public void Configure(EntityTypeBuilder<LlmCall> b)
    {
        b.Property(x => x.Task).HasConversion<string>();
        b.Property(x => x.Request).HasColumnType("jsonb");
        b.HasIndex(x => x.CreatedAt);
    }
}

internal sealed class AppSettingConfiguration : IEntityTypeConfiguration<AppSetting>
{
    public void Configure(EntityTypeBuilder<AppSetting> b) => b.HasIndex(x => x.Key).IsUnique();
}
