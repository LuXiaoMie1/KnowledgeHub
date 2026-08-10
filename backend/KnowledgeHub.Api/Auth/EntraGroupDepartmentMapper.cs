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
    /// 用 List 而非直接綁定 Dictionary：Dictionary 的列舉順序不受保證，但
    /// 「使用者屬於多個已映射群組時取第一個命中」需要確定順序，所以直接用
    /// IConfiguration.GetChildren() 保留設定檔裡的原始宣告順序。
    /// </summary>
    public static IReadOnlyList<KeyValuePair<string, string>> LoadGroupDepartmentMap(IConfiguration configuration) =>
        configuration.GetSection("Entra:GroupDepartmentMap").GetChildren()
            .Select(c => new KeyValuePair<string, string>(c.Key, c.Value ?? ""))
            .ToList();

    /// <summary>
    /// 找映射表中第一個命中使用者 groups claim 的項目，加上 department claim。
    /// 沒有任何命中就不加 claim——下游 ICurrentUser.Department 會因缺 claim
    /// 丟 InvalidOperationException，維持既有「查無部門即拒絕」的行為。
    /// 命中多個已映射群組時取映射表第一個並記 log warning（聯集檢索是後續
    /// 獨立工作，本階段先簡化成單一部門，不在此次範圍內處理）。
    /// </summary>
    public static void ApplyDepartmentClaim(
        ClaimsIdentity identity,
        IReadOnlyList<KeyValuePair<string, string>> groupDepartmentMap,
        ILogger logger)
    {
        var groups = identity.FindAll(GroupsClaimType).Select(c => c.Value).ToHashSet();
        if (groups.Count == 0) return;

        var hits = groupDepartmentMap.Where(kv => groups.Contains(kv.Key)).ToList();
        if (hits.Count == 0) return;

        if (hits.Count > 1)
        {
            logger.LogWarning(
                "使用者屬於多個已映射部門的群組（{Count} 個命中），取映射表第一個命中的部門 {Department}；" +
                "聯集檢索為後續獨立工作，本次先簡化成單一部門。",
                hits.Count, hits[0].Value);
        }

        identity.AddClaim(new Claim(DepartmentClaimType, hits[0].Value));
    }
}
