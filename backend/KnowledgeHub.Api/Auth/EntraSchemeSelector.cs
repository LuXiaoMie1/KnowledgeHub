using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace KnowledgeHub.Api.Auth;

/// <summary>
/// Policy scheme 的 ForwardDefaultSelector：只看 token 的 issuer（不驗證簽章，
/// 簽章驗證交給被轉去的那個 scheme 做），決定轉給 Entra scheme 還是既有自簽
/// JWT scheme。獨立成靜態方法，讓 KnowledgeHub.Tests 能直接餵假 HttpContext
/// 驗證分流邏輯，不必掛起整個 authentication pipeline。
/// </summary>
public static class EntraSchemeSelector
{
    public const string PolicySchemeName = "Dynamic";
    public const string EntraSchemeName = "Entra";

    private static readonly string[] EntraIssuerMarkers = ["login.microsoftonline.com", "sts.windows.net"];

    public static string Select(HttpContext context)
    {
        var header = context.Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return JwtBearerDefaults.AuthenticationScheme;

        var token = header["Bearer ".Length..].Trim();
        try
        {
            var issuer = new JwtSecurityTokenHandler().ReadJwtToken(token).Issuer;
            return EntraIssuerMarkers.Any(m => issuer.Contains(m, StringComparison.OrdinalIgnoreCase))
                ? EntraSchemeName
                : JwtBearerDefaults.AuthenticationScheme;
        }
        catch (Exception)
        {
            // 不是合法 JWT 格式（缺分段、非 base64url 等）：交回既有自簽 JWT scheme，
            // 讓它照原本流程驗證失敗回 401，而不是在分流階段就整個請求炸掉。
            return JwtBearerDefaults.AuthenticationScheme;
        }
    }
}
