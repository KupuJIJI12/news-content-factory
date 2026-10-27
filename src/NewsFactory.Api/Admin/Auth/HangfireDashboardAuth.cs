using Hangfire.Dashboard;

namespace NewsFactory.Api.Admin.Auth;

public sealed class HangfireDashboardAuth : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context) =>
        context.GetHttpContext().User.Identity?.IsAuthenticated == true;
}
