using System.Security.Claims;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace KnowledgeHub.Api.Auth;

/// <summary>
/// 把 Entra ID token 的 "groups" claim（群組 Object ID）比對設定檔映射表，
/// 產生與既有自簽 JWT 相同格式的 "department" claim，讓下游 ICurrentUser／
/// 部門過濾邏輯不必分辨 token 來源。獨立成靜態方法，讓 KnowledgeHub.Tests
/// 能直接呼叫做回歸測試，不必把整個 host（Entra 中介軟體、DB）都跑起來。
/// </summary>
public static class EntraGroupDepartmentMapper
{
    public const string GroupsClaimType = "groups";
    public const string DepartmentClaimType = "department";

    /// <summary>
    /// 依 appsettings 裡 Entra:GroupDepartmentMap 的宣告順序讀出映射表。
    /// 用 List 而非直接綁定 Dictionary：Dictionary 的列舉順序不受保證。
    /// </summary>
    public static IReadOnlyList<KeyValuePair<string, string>> LoadGroupDepartmentMap(IConfiguration configuration) =>
        configuration.GetSection("Entra:GroupDepartmentMap").GetChildren()
            .Select(c => new KeyValuePair<string, string>(c.Key, c.Value ?? ""))
            .ToList();

    /// <summary>
    /// 找映射表中所有命中使用者 groups claim 的項目，逐一加上 department claim
    /// （多部門聯集檢索需要使用者所屬的每一個部門，不能只取第一個）。
    /// 沒有任何命中就不加 claim——下游 ICurrentUser.Department／Departments 會因缺 claim
    /// 丟 InvalidOperationException，維持既有「查無部門即拒絕」的行為。
    /// </summary>
    public static void ApplyDepartmentClaim(
        ClaimsIdentity identity,
        IReadOnlyList<KeyValuePair<string, string>> groupDepartmentMap,
        ILogger logger)
    {
        var groups = identity.FindAll(GroupsClaimType).Select(c => c.Value).ToHashSet();
        if (groups.Count == 0) return;

        var departments = groupDepartmentMap
            .Where(kv => groups.Contains(kv.Key))
            .Select(kv => kv.Value)
            .Distinct()
            .ToList();
        if (departments.Count == 0) return;

        if (departments.Count > 1)
        {
            logger.LogInformation(
                "使用者屬於多個已映射部門的群組，加上 {Count} 個 department claim：{Departments}",
                departments.Count, string.Join(", ", departments));
        }

        foreach (var department in departments)
            identity.AddClaim(new Claim(DepartmentClaimType, department));
    }
}
