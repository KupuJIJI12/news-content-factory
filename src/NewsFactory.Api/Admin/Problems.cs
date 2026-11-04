namespace NewsFactory.Api.Admin;

/// <summary>application/problem+json responses with a machine-readable "code" extension.</summary>
public static class Problems
{
    public static IResult NotFound(string what) => Problem(StatusCodes.Status404NotFound, "not_found", $"{what} не найден");

    public static IResult Validation(string message, IDictionary<string, string[]>? errors = null) =>
        Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: message,
            extensions: errors is null
                ? new Dictionary<string, object?> { ["code"] = "validation" }
                : new Dictionary<string, object?> { ["code"] = "validation", ["errors"] = errors });

    public static IResult InvalidStatus(string message) => Problem(StatusCodes.Status409Conflict, "invalid_status", message);

    public static IResult Conflict(string code, string message, object? extra = null) =>
        Problem(StatusCodes.Status409Conflict, code, message, extra);

    public static IResult Problem(int status, string code, string message, object? extra = null)
    {
        var ext = new Dictionary<string, object?> { ["code"] = code };
        if (extra is not null)
            foreach (var p in extra.GetType().GetProperties())
                ext[char.ToLowerInvariant(p.Name[0]) + p.Name[1..]] = p.GetValue(extra);
        return Results.Problem(statusCode: status, title: message, extensions: ext);
    }
}
