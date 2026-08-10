using System.Security.Claims;
using KnowledgeHub.Api.Auth;
using Microsoft.Extensions.Logging.Abstractions;

// 回歸測試：Entra token 的 "groups" claim 比對設定檔映射表產生 "department" claim，
// 下游 ICurrentUser／部門過濾要吃到跟自簽 JWT 一樣格式的 claim，見
// EntraGroupDepartmentMapper 的類別註解。以下 GUID 皆為假值，不是真實租戶／群組 ID
// （真實值一律走 appsettings.Local.json 或 user-secrets，不進版控，見 README）。
public class EntraGroupDepartmentMapperTests
{
    private const string ItGroupId = "aaaaaaaa-0000-0000-0000-000000000001";
    private const string HrGroupId = "aaaaaaaa-0000-0000-0000-000000000002";
    private const string UnmappedGroupId = "aaaaaaaa-0000-0000-0000-000000000099";

    // 宣告順序：IT 在前、HR 在後——「多個命中取第一個」依這個順序判斷。
    private static readonly IReadOnlyList<KeyValuePair<string, string>> Map =
    [
        new(ItGroupId, "IT"),
        new(HrGroupId, "HR")
    ];

    private static ClaimsIdentity IdentityWithGroups(params string[] groupIds)
    {
        var identity = new ClaimsIdentity();
        foreach (var g in groupIds) identity.AddClaim(new Claim("groups", g));
        return identity;
    }

    [Fact]
    public void groups含已映射群組ID_產生對應部門claim()
    {
        var identity = IdentityWithGroups(ItGroupId);

        EntraGroupDepartmentMapper.ApplyDepartmentClaim(identity, Map, NullLogger.Instance);

        Assert.Equal("IT", identity.FindFirst("department")?.Value);
    }

    [Fact]
    public void groups全部未映射_不加department_claim()
    {
        var identity = IdentityWithGroups(UnmappedGroupId);

        EntraGroupDepartmentMapper.ApplyDepartmentClaim(identity, Map, NullLogger.Instance);

        Assert.Null(identity.FindFirst("department"));
    }

    [Fact]
    public void 多個已映射群組命中_取映射表第一個命中()
    {
        // 使用者同時屬於 HR 與 IT 群組（token 內順序故意反過來），
        // 映射表宣告順序 IT 在前 → 應取 IT，不是 HR。
        var identity = IdentityWithGroups(HrGroupId, ItGroupId);

        EntraGroupDepartmentMapper.ApplyDepartmentClaim(identity, Map, NullLogger.Instance);

        Assert.Equal("IT", identity.FindFirst("department")?.Value);
    }

    [Fact]
    public void 沒有groups_claim_不加department_claim也不丟例外()
    {
        var identity = new ClaimsIdentity(); // token 裡完全沒有 groups claim

        EntraGroupDepartmentMapper.ApplyDepartmentClaim(identity, Map, NullLogger.Instance);

        Assert.Null(identity.FindFirst("department"));
    }
}
