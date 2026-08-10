using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using KnowledgeHub.Api.Auth;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;

// 回歸測試：policy scheme 依 token issuer 分流到 Entra 或既有自簽 JWT scheme，
// 兩種 token 都要能過 [Authorize]，見 EntraSchemeSelector 的類別註解。
public class EntraSchemeSelectorTests
{
    private static DefaultHttpContext ContextWithBearer(string? token)
    {
        var ctx = new DefaultHttpContext();
        if (token is not null) ctx.Request.Headers.Authorization = $"Bearer {token}";
        return ctx;
    }

    private static string UnsignedJwt(string issuer) =>
        new JwtSecurityTokenHandler().WriteToken(
            new JwtSecurityToken(issuer: issuer, claims: [new Claim("sub", "x")]));

    [Theory]
    [InlineData("https://login.microsoftonline.com/aaaaaaaa-0000-0000-0000-000000000000/v2.0")]
    [InlineData("https://sts.windows.net/aaaaaaaa-0000-0000-0000-000000000000/")]
    public void issuer含Entra網域_分流到Entra_scheme(string issuer)
    {
        var scheme = EntraSchemeSelector.Select(ContextWithBearer(UnsignedJwt(issuer)));

        Assert.Equal(EntraSchemeSelector.EntraSchemeName, scheme);
    }

    [Fact]
    public void issuer是既有自簽發行者_分流到既有Bearer_scheme()
    {
        var scheme = EntraSchemeSelector.Select(ContextWithBearer(UnsignedJwt("KnowledgeHub")));

        Assert.Equal(JwtBearerDefaults.AuthenticationScheme, scheme);
    }

    [Fact]
    public void 沒有Authorization_header_分流到既有Bearer_scheme讓既有401流程接手()
    {
        var scheme = EntraSchemeSelector.Select(ContextWithBearer(null));

        Assert.Equal(JwtBearerDefaults.AuthenticationScheme, scheme);
    }

    [Fact]
    public void 不是合法JWT格式_分流到既有Bearer_scheme而非丟例外()
    {
        var scheme = EntraSchemeSelector.Select(ContextWithBearer("not-a-jwt"));

        Assert.Equal(JwtBearerDefaults.AuthenticationScheme, scheme);
    }
}
