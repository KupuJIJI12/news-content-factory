using System.Text;
using Microsoft.EntityFrameworkCore;
using NewsFactory.Api.Admin.Settings;
using NewsFactory.Domain;
using NewsFactory.Domain.Entities;
using NewsFactory.Infrastructure;

namespace NewsFactory.Api.Admin.Cities;

public sealed record CityDto(long Id, string Name, string Slug, string TimeZone, IReadOnlyList<string> Keywords, int ScoreThreshold,
    ChannelSettingsDto? Channel, int SourcesCount);

public sealed record CityInput(string? Name, string? TimeZone, IReadOnlyList<string>? Keywords, int ScoreThreshold);

/// <summary>Cities with their keywords, threshold and the Max channel; each city gets a (disabled) channel on creation.</summary>
public static class CityEndpoints
{
    public static void MapCityEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/cities", async (NewsFactoryDbContext db, CancellationToken ct) => await ListAsync(db, ct));

        api.MapPost("/cities", async (CityInput input, NewsFactoryDbContext db, CancellationToken ct) =>
        {
            var (errors, keywords) = Validate(input);
            if (errors.Count > 0) return Problems.Validation("Проверьте настройки города", errors);
            var name = input.Name!.Trim();
            if (await NameTakenAsync(db, name, null, ct)) return Problems.Conflict("conflict", $"Город «{name}» уже есть");

            var city = new City
            {
                Name = name, Slug = await UniqueSlugAsync(db, Slug(name), ct), TimeZone = input.TimeZone!.Trim(),
                Keywords = keywords, ScoreThreshold = input.ScoreThreshold,
            };
            db.Cities.Add(city);
            await db.SaveChangesAsync(ct);
            db.Channels.Add(new Channel { CityId = city.Id, Platform = Platform.Max, Name = $"{name} — Max", DailyLimit = 4, IsEnabled = false });
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/cities/{city.Id}", (await ListAsync(db, ct)).Single(c => c.Id == city.Id));
        });

        api.MapPut("/cities/{id:long}", async (long id, CityInput input, NewsFactoryDbContext db, CancellationToken ct) =>
        {
            var city = await db.Cities.SingleOrDefaultAsync(c => c.Id == id, ct);
            if (city is null) return Problems.NotFound("Город");
            var (errors, keywords) = Validate(input);
            if (errors.Count > 0) return Problems.Validation("Проверьте настройки города", errors);
            var name = input.Name!.Trim();
            if (await NameTakenAsync(db, name, id, ct)) return Problems.Conflict("conflict", $"Город «{name}» уже есть");
            city.Name = name;
            city.TimeZone = input.TimeZone!.Trim();
            city.Keywords = keywords;
            city.ScoreThreshold = input.ScoreThreshold;
            await db.SaveChangesAsync(ct);
            return Results.Ok((await ListAsync(db, ct)).Single(c => c.Id == id));
        });

        api.MapPut("/cities/{id:long}/channel", async (long id, ChannelSettingsInput input, NewsFactoryDbContext db, CancellationToken ct) =>
        {
            var city = await db.Cities.SingleOrDefaultAsync(c => c.Id == id, ct);
            if (city is null) return Problems.NotFound("Город");
            if (ChannelRules.Validate(input, nameRequired: false) is { Count: > 0 } errors)
                return Problems.Validation("Проверьте настройки канала", errors);
            var channel = await db.Channels.SingleOrDefaultAsync(c => c.CityId == id && c.Platform == Platform.Max, ct);
            if (channel is null) db.Channels.Add(channel = new Channel { CityId = id, Platform = Platform.Max });
            ChannelRules.Apply(channel, input, defaultName: $"{city.Name} — Max");
            await db.SaveChangesAsync(ct);
            return Results.Ok(ChannelRules.ToDto(channel, city));
        });
    }

    private static async Task<List<CityDto>> ListAsync(NewsFactoryDbContext db, CancellationToken ct)
    {
        var cities = await db.Cities.OrderBy(c => c.Id).ToListAsync(ct);
        var channels = await db.Channels.Where(c => c.Platform == Platform.Max).ToListAsync(ct);
        var sourceCounts = await db.SourceCities.GroupBy(sc => sc.CityId).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        return cities.Select(c => new CityDto(c.Id, c.Name, c.Slug, c.TimeZone, c.Keywords, c.ScoreThreshold,
            channels.FirstOrDefault(ch => ch.CityId == c.Id) is { } ch ? ChannelRules.ToDto(ch, c) : null,
            sourceCounts.FirstOrDefault(x => x.Key == c.Id)?.Count ?? 0)).ToList();
    }

    private static (Dictionary<string, string[]> Errors, List<string> Keywords) Validate(CityInput input)
    {
        var keywords = (input.Keywords ?? []).Select(k => k.Trim().ToLowerInvariant()).Where(k => k.Length > 0).Distinct().ToList();
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(input.Name)) errors["name"] = ["Укажите название города"];
        if (string.IsNullOrWhiteSpace(input.TimeZone) || !TimeZoneInfo.TryFindSystemTimeZoneById(input.TimeZone.Trim(), out _))
            errors["timeZone"] = ["Неизвестный часовой пояс (например, Europe/Samara)"];
        if (keywords.Count == 0) errors["keywords"] = ["Нужно хотя бы одно ключевое слово"];
        if (input.ScoreThreshold is < 1 or > 10) errors["scoreThreshold"] = ["Порог — от 1 до 10"];
        return (errors, keywords);
    }

    private static async Task<bool> NameTakenAsync(NewsFactoryDbContext db, string name, long? exceptId, CancellationToken ct)
    {
        var lower = name.ToLowerInvariant();
        return await db.Cities.AnyAsync(c => c.Name.ToLower() == lower && c.Id != exceptId, ct);
    }

    private static async Task<string> UniqueSlugAsync(NewsFactoryDbContext db, string slug, CancellationToken ct)
    {
        var candidate = slug;
        for (var n = 2; await db.Cities.AnyAsync(c => c.Slug == candidate, ct); n++) candidate = $"{slug}-{n}";
        return candidate;
    }

    private static readonly Dictionary<char, string> Translit = new()
    {
        ['а'] = "a", ['б'] = "b", ['в'] = "v", ['г'] = "g", ['д'] = "d", ['е'] = "e", ['ё'] = "e", ['ж'] = "zh", ['з'] = "z", ['и'] = "i",
        ['й'] = "y", ['к'] = "k", ['л'] = "l", ['м'] = "m", ['н'] = "n", ['о'] = "o", ['п'] = "p", ['р'] = "r", ['с'] = "s", ['т'] = "t",
        ['у'] = "u", ['ф'] = "f", ['х'] = "kh", ['ц'] = "ts", ['ч'] = "ch", ['ш'] = "sh", ['щ'] = "shch", ['ъ'] = "", ['ы'] = "y", ['ь'] = "",
        ['э'] = "e", ['ю'] = "yu", ['я'] = "ya",
    };

    public static string Slug(string name)
    {
        var sb = new StringBuilder();
        foreach (var ch in name.ToLowerInvariant())
        {
            if (Translit.TryGetValue(ch, out var t)) sb.Append(t);
            else if (ch is >= 'a' and <= 'z' or >= '0' and <= '9') sb.Append(ch);
            else if (sb.Length > 0 && sb[^1] != '-') sb.Append('-');
        }
        var slug = sb.ToString().Trim('-');
        return slug.Length > 0 ? slug : "city";
    }
}
