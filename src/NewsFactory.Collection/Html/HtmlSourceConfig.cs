namespace NewsFactory.Collection.Html;

/// <summary>Stored as camelCase JSON in Source.Config.</summary>
public sealed record HtmlSourceConfig(
    string ListUrl,
    string ItemSelector,
    string LinkSelector,
    string? TitleSelector = null,
    string? DateSelector = null,
    string? DateFormat = null,
    string? BodySelector = null,
    string? ImageSelector = null);
