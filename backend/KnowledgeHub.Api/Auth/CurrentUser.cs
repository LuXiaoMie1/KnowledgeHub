using KnowledgeHub.Core.Interfaces;

namespace KnowledgeHub.Api.Auth;

public class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public IReadOnlyList<string> Departments
    {
        get
        {
            var departments = accessor.HttpContext?.User.FindAll("department")
                .Select(c => c.Value).Distinct().ToList();
            if (departments is null || departments.Count == 0)
                throw new InvalidOperationException("缺少 department claim");
            return departments;
        }
    }

    public string Department => Departments.Count == 1
        ? Departments[0]
        : throw new InvalidOperationException("使用者屬於多個部門，無法使用單一部門語意");

    public string Username =>
        accessor.HttpContext?.User.FindFirst("sub")?.Value
        ?? throw new InvalidOperationException("缺少 sub claim");

    public string UserKey =>
        accessor.HttpContext?.User.FindFirst("oid")?.Value
        ?? accessor.HttpContext?.User.FindFirst("sub")?.Value
        ?? throw new InvalidOperationException("缺少 oid/sub claim");
}
