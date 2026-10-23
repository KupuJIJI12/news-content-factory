using Microsoft.EntityFrameworkCore;

namespace NewsFactory.Infrastructure;

public static class PostgresLocks
{
    private const long CityNamespace = 1001;

    /// <summary>Transaction-scoped advisory lock per city. Requires an open transaction on <paramref name="db"/>.</summary>
    public static async Task AcquireCityLockAsync(NewsFactoryDbContext db, long cityId, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Блокировка города требует открытой транзакции");
        var key = (CityNamespace << 32) | (cityId & 0xFFFFFFFF);
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({key})", ct);
    }
}
