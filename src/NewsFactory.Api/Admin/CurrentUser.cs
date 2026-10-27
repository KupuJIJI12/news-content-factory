using System.Security.Claims;

namespace NewsFactory.Api.Admin;

public static class CurrentUser
{
    public static long Id(this ClaimsPrincipal user) =>
        long.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new InvalidOperationException("Пользователь не аутентифицирован"));
}
