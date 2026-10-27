using Microsoft.EntityFrameworkCore;
using NewsFactory.Collection;
using NewsFactory.Domain.Entities;
using NewsFactory.Infrastructure;

namespace NewsFactory.Api.Admin.Sources;

public static class SourceEndpoints
{
    public static void MapSourceEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/sources", async (long? cityId, NewsFactoryDbContext db, TimeProvider time, CancellationToken ct) =>
        {
            var now = time.GetUtcNow();
            var sources = await db.Sources.Include(s => s.Cities).ThenInclude(c => c.City)
                .Where(s => cityId == null || s.Cities.Any(c => c.CityId == cityId)).ToListAsync(ct);
            return sources.OrderBy(s => s.Name, StringComparer.Ordinal).Select(s => ToDto(s, now)).ToList();
        });

        api.MapPost("/sources", async (SourceInput input, NewsFactoryDbContext db, TimeProvider time, CancellationToken ct) =>
        {
            if (input.Validate() is { Count: > 0 } errors) return Problems.Validation("Проверьте поля источника", errors);
            if (await UnknownCitiesAsync(db, input, ct) is { } unknown) return unknown;
            if (await UrlTakenAsync(db, input, null, ct)) return Problems.Conflict("conflict", "У этого города уже есть источник с таким адресом");
            var source = new Source();
            Apply(source, input);
            SyncCities(source, input);
            db.Sources.Add(source);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/sources/{source.Id}", await DtoAsync(db, source.Id, time.GetUtcNow(), ct));
        });

        api.MapPut("/sources/{id:long}", async (long id, SourceInput input, NewsFactoryDbContext db, TimeProvider time, CancellationToken ct) =>
        {
            var source = await db.Sources.Include(s => s.Cities).SingleOrDefaultAsync(s => s.Id == id, ct);
            if (source is null) return Problems.NotFound("Источник");
            if (input.Validate() is { Count: > 0 } errors) return Problems.Validation("Проверьте поля источника", errors);
            if (await UnknownCitiesAsync(db, input, ct) is { } unknown) return unknown;
            if (await UrlTakenAsync(db, input, id, ct))
                return Problems.Conflict("conflict", "У этого города уже есть источник с таким адресом");
            Apply(source, input);
            SyncCities(source, input);
            await db.SaveChangesAsync(ct);
            return Results.Ok(await DtoAsync(db, id, time.GetUtcNow(), ct));
        });

        api.MapPost("/sources/autodetect", async (AutoSetupRequest r, SourceAutoSetup setup, CancellationToken ct) =>
            Uri.TryCreate(r.Url?.Trim(), UriKind.Absolute, out var url) && url.Scheme is "http" or "https"
                ? Results.Ok(await setup.RunAsync(url, ct))
                : Problems.Validation("Проверьте адрес", new Dictionary<string, string[]> { ["url"] = ["Нужен полный адрес http(s)://…"] }));

        api.MapPost("/sources/test", async (SourceInput input, SourceTester tester, CancellationToken ct) =>
            input.Validate(requireCities: false) is { Count: > 0 } errors
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

    /// <summary>Adds, updates and removes the source's city links to match the input.</summary>
    private static void SyncCities(Source source, SourceInput input)
    {
        var wanted = input.Cities!.ToDictionary(c => c.CityId, c => c.IsDedicated);
        source.Cities.RemoveAll(link => !wanted.ContainsKey(link.CityId));
        foreach (var (cityId, dedicated) in wanted)
        {
            var link = source.Cities.SingleOrDefault(l => l.CityId == cityId);
            if (link is null) source.Cities.Add(new SourceCity { Source = source, CityId = cityId, IsDedicated = dedicated });
            else link.IsDedicated = dedicated;
        }
    }

    private static Task<bool> UrlTakenAsync(NewsFactoryDbContext db, SourceInput input, long? exceptId, CancellationToken ct)
    {
        var url = input.Url!.Trim();
        var cityId = input.Cities![0].CityId;
        return db.Sources.AnyAsync(s => s.Url == url && s.Id != exceptId && s.Cities.Any(c => c.CityId == cityId), ct);
    }

    private static async Task<IResult?> UnknownCitiesAsync(NewsFactoryDbContext db, SourceInput input, CancellationToken ct)
    {
        var ids = input.Cities!.Select(c => c.CityId).ToList();
        var found = await db.Cities.CountAsync(c => ids.Contains(c.Id), ct);
        return found == ids.Count ? null
            : Problems.Validation("Проверьте поля источника", new Dictionary<string, string[]> { ["cities"] = ["Такого города нет"] });
    }

    private static async Task<SourceDto> DtoAsync(NewsFactoryDbContext db, long id, DateTimeOffset now, CancellationToken ct) =>
        ToDto(await db.Sources.Include(s => s.Cities).ThenInclude(c => c.City).AsNoTracking().SingleAsync(s => s.Id == id, ct), now);

    private static SourceDto ToDto(Source s, DateTimeOffset now) => new(
        s.Id, s.Name, s.Type, s.Url, s.Config, s.PollIntervalMinutes, s.ExpectedFrequencyHours, s.PhotosAllowedWithCredit, s.PhotoCredit,
        s.IsEnabled, s.Cities.OrderBy(c => c.CityId).Select(c => new SourceCityDto(c.CityId, c.City?.Name ?? "", c.IsDedicated)).ToList(),
        s.LastRunAt, s.LastSuccessAt, s.LastItemAt, s.ConsecutiveFailures, s.LastError, SourceHealth.Evaluate(s, now));
}
