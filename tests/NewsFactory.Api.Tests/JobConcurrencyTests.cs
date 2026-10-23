using System.Reflection;
using Hangfire;
using NewsFactory.Collection.Jobs;
using NewsFactory.Processing.Jobs;

namespace NewsFactory.Api.Tests;

/// <summary>Hangfire locks must be per entity id, not one global lock per method.</summary>
public class JobConcurrencyTests
{
    private static string? LockResource(Type job)
    {
        var attr = job.GetMethod("RunAsync")!.GetCustomAttribute<DisableConcurrentExecutionAttribute>();
        if (attr is null) return null;
        // Hangfire keeps the resource format in a private string field; read whichever string member holds it.
        var members = attr.GetType().GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .Where(f => f.FieldType == typeof(string)).Select(f => (string?)f.GetValue(attr))
            .Concat(attr.GetType().GetProperties().Where(p => p.PropertyType == typeof(string)).Select(p => (string?)p.GetValue(attr)));
        return members.FirstOrDefault(v => v is not null && v.Contains(':')) ?? "<attribute without per-id resource>";
    }

    [Fact]
    public void Collect_source_is_locked_per_source() => Assert.Equal("collect-source:{0}", LockResource(typeof(CollectSourceJob)));

    [Fact]
    public void Process_story_is_locked_per_story() => Assert.Equal("process-story:{0}", LockResource(typeof(ProcessStoryJob)));
}
