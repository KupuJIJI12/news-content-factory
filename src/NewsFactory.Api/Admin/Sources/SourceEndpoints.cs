using Microsoft.EntityFrameworkCore;
using NewsFactory.Collection;
using NewsFactory.Domain.Entities;
using NewsFactory.Infrastructure;

namespace NewsFactory.Api.Admin.Sources;

public static class SourceEndpoints
{
    public static void MapSourceEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/sources", async (NewsFactoryDbContext db, TimeProvider time, CancellationToken ct) =>
        {
            var cityId = await FirstCityIdAsync(db, ct);
            var now = time.GetUtcNow();
            var sources = await db.Sources.Include(s => s.Cities).OrderBy(s => s.Name).ToListAsync(ct);
            return sources.OrderBy(s => s.Name, StringComparer.Ordinal).Select(s => ToDto(s, cityId, now)).ToList();
        });

        api.MapPost("/sources", async (SourceInput input, NewsFactoryDbContext db, TimeProvider time, CancellationToken ct) =>
        {
            if (input.Validate() is { Count: > 0 } errors) return Problems.Validation("Проверьте поля источника", errors);
            if (await db.Sources.AnyAsync(s => s.Url == input.Url, ct)) return Problems.Conflict("conflict", "Источник с таким адресом уже есть");
            var cityId = await FirstCityIdAsync(db, ct);
            var source = new Source();
            Apply(source, input);
            source.Cities.Add(new SourceCity { Source = source, CityId = cityId, IsDedicated = input.IsDedicated });
            db.Sources.Add(source);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/sources/{source.Id}", ToDto(source, cityId, time.GetUtcNow()));
        });

        api.MapPut("/sources/{id:long}", async (long id, SourceInput input, NewsFactoryDbContext db, TimeProvider time, CancellationToken ct) =>
        {
            var source = await db.Sources.Include(s => s.Cities).SingleOrDefaultAsync(s => s.Id == id, ct);
            if (source is null) return Problems.NotFound("Источник");
            if (input.Validate() is { Count: > 0 } errors) return Problems.Validation("Проверьте поля источника", errors);
            if (await db.Sources.AnyAsync(s => s.Url == input.Url && s.Id != id, ct))
                return Problems.Conflict("conflict", "Источник с таким адресом уже есть");
            var cityId = await FirstCityIdAsync(db, ct);
            Apply(source, input);
            var link = source.Cities.SingleOrDefault(c => c.CityId == cityId);
            if (link is null) source.Cities.Add(new SourceCity { Source = source, CityId = cityId, IsDedicated = input.IsDedicated });
            else link.IsDedicated = input.IsDedicated;
            await db.SaveChangesAsync(ct);
            return Results.Ok(ToDto(source, cityId, time.GetUtcNow()));
        });

        api.MapPost("/sources/test", async (SourceInput input, SourceTester tester, CancellationToken ct) =>
            input.Validate() is { Count: > 0 } errors
                ? Problems.Validation("Проверьте поля источника", errors)
                : Results.Ok(await tester.RunAsync(input, ct)));
    }

    private static void Apply(Source s, SourceInput i)
    {
        s.Name = i.Name!.Trim();
        s.Type = i.Type;
        s.Url = i.Url!.Trim();
        s.Config = string.IsNullOrWhiteSpace(i.Config) ? null : i.Config;
        s.PollIntervalMinutes = i.PollIntervalMinutes;
        s.ExpectedFrequencyHours = i.ExpectedFrequencyHours;
        s.PhotosAllowedWithCredit = i.PhotosAllowedWithCredit;
        s.PhotoCredit = string.IsNullOrWhiteSpace(i.PhotoCredit) ? null : i.PhotoCredit.Trim();
        s.IsEnabled = i.IsEnabled;
    }

    private static SourceDto ToDto(Source s, long cityId, DateTimeOffset now) => new(
        s.Id, s.Name, s.Type, s.Url, s.Config, s.PollIntervalMinutes, s.ExpectedFrequencyHours, s.PhotosAllowedWithCredit, s.PhotoCredit,
        s.IsEnabled, s.Cities.Any(c => c.CityId == cityId && c.IsDedicated), s.LastRunAt, s.LastSuccessAt, s.LastItemAt,
        s.ConsecutiveFailures, s.LastError, SourceHealth.Evaluate(s, now));

    private static Task<long> FirstCityIdAsync(NewsFactoryDbContext db, CancellationToken ct) =>
        db.Cities.OrderBy(c => c.Id).Select(c => c.Id).FirstAsync(ct);
}
