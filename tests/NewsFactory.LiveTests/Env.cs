namespace NewsFactory.LiveTests;

internal static class Env
{
    public static string? Get(string name) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } v ? v : null;

    public static void Require(params string[] names)
    {
        var missing = names.Where(n => Get(n) is null).ToList();
        Assert.SkipWhen(missing.Count > 0, "Не заданы переменные: " + string.Join(", ", missing));
    }

    public static void Log(string line) => TestContext.Current.TestOutputHelper?.WriteLine(line);
}
