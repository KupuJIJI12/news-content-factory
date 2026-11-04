using Microsoft.EntityFrameworkCore;
using NewsFactory.Domain.Entities;
using NewsFactory.Infrastructure;

namespace NewsFactory.Collection;

/// <summary>
/// Global "freeze" of scheduled news collection (owner's switch in the admin header).
/// Only the scheduled poll stops; already collected items are still processed, and manual source tests keep working.
/// </summary>
public sealed class CollectionPause(NewsFactoryDbContext db)
{
    public const string SettingKey = "collection.paused";

    public async Task<bool> IsPausedAsync(CancellationToken ct) =>
        await db.AppSettings.Where(s => s.Key == SettingKey).Select(s => s.Value).FirstOrDefaultAsync(ct) == "true";

    public async Task SetAsync(bool paused, CancellationToken ct)
    {
        var setting = await db.AppSettings.SingleOrDefaultAsync(s => s.Key == SettingKey, ct);
        if (setting is null) db.AppSettings.Add(setting = new AppSetting { Key = SettingKey });
        setting.Value = paused ? "true" : "false";
        await db.SaveChangesAsync(ct);
    }
}
