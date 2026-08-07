using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace KnowledgeHub.Api.Auth;

/// <summary>
/// 獨立成方法，讓 <see cref="KnowledgeHub.Tests"/> 裡的
/// JwtClaimMappingTests 能直接呼叫同一份設定邏輯做回歸測試——
/// 不必整個 host（DB、Hangfire、Vertex 憑證）都跑起來才能驗證 claim 映射行為。
/// </summary>
public static class JwtBearerConfigurator
{
    public static void Configure(JwtBearerOptions o, string signingKey, string? issuer, string? audience)
    {
        // JwtSecurityTokenHandler/JsonWebTokenHandler 預設會把 "sub" 這類標準 claim
        // 自動改名成長版 URI（ClaimTypes.NameIdentifier），導致 CurrentUser 用字面
        // "sub" 找不到值而丟例外；關掉這個舊行為，才能保留 "department"/"sub" 原始 claim 名。
        // 絕不可移除或改回 true——移除後 CurrentUser.Username 會在每個要求 email 的
        // 對話請求裡丟例外。回歸測試見 JwtClaimMappingTests，這行被拿掉測試會翻紅。
        o.MapInboundClaims = false;
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = issuer,
            ValidAudience = audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
            NameClaimType = "sub"
        };
    }
}
