using NewsFactory.TestSupport;

namespace NewsFactory.Infrastructure.Tests;

[Collection("db")]
public class PostgresLocksTests(PostgresFixture db)
{
    [Fact]
    public async Task Second_transaction_waits_for_city_lock()
    {
        await using var a = db.CreateContext();
        await using var b = db.CreateContext();
        await using var ta = await a.Database.BeginTransactionAsync();
        await PostgresLocks.AcquireCityLockAsync(a, 1, default);
        await using var tb = await b.Database.BeginTransactionAsync();
        var waiting = PostgresLocks.AcquireCityLockAsync(b, 1, default);
        await Task.Delay(300);
        Assert.False(waiting.IsCompleted);
        await ta.CommitAsync();
        await waiting.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Different_cities_do_not_block_each_other()
    {
        await using var a = db.CreateContext();
        await using var b = db.CreateContext();
        await using var ta = await a.Database.BeginTransactionAsync();
        await PostgresLocks.AcquireCityLockAsync(a, 1, default);
        await using var tb = await b.Database.BeginTransactionAsync();
        await PostgresLocks.AcquireCityLockAsync(b, 2, default).WaitAsync(TimeSpan.FromSeconds(2));
    }
}
