using NewsFactory.Domain;

namespace NewsFactory.Processing.Tests;

public class DeterministicFactCheckerTests
{
    private const string Src = "15 ноября с 10:00 до 17.00 на ул. Гагарина, 47 не будет воды. Затронуты 1,5 тыс. жителей.";

    [Fact]
    public void Equivalent_forms_pass() =>
        Assert.Empty(DeterministicFactChecker.Check(
            "**Без воды 15.11!**\n\nС 10.00 до 17:00, Гагарина 47, 1.5 тыс. человек. #Отрадный2026", Src));

    [Fact]
    public void Invented_number_and_date_are_reported()
    {
        var issues = DeterministicFactChecker.Check("16 ноября без воды останутся 3 тысячи жителей дома 47", Src);
        Assert.Equal(["16 ноября", "3"], issues.Select(i => i.Claim));
        Assert.All(issues, i => Assert.Equal(FactCheckIssueSource.Deterministic, i.Source));
        Assert.All(issues, i => Assert.Equal("Нет в источниках", i.Problem));
    }

    [Fact]
    public void Invented_time_is_reported() =>
        Assert.Equal(["9:00"], DeterministicFactChecker.Check("Воду отключат с 9:00", Src).Select(i => i.Claim));

    [Fact]
    public void Hours_word_form_matches_clock_time() =>
        Assert.Empty(DeterministicFactChecker.Check("Отключение начнётся в 10 часов", Src));

    [Fact]
    public void Duplicates_are_reported_once() =>
        Assert.Single(DeterministicFactChecker.Check("3 дома и ещё 3 дома", Src));
}
