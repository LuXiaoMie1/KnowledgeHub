using KnowledgeHub.Api.Auth;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.JsonWebTokens;

// 回歸測試：Program.cs 用 JwtBearerConfigurator.Configure 設定 JwtBearerOptions，
// 其中 MapInboundClaims = false 是關鍵——拿掉它，JsonWebTokenHandler 會把 "sub"
// 這種標準 claim 自動改名成長版 URI（ClaimTypes.NameIdentifier），CurrentUser.Username
// 用字面 "sub" 找值就會失敗（實際症狀：對話要求寄信會整段丟例外）。
// 這裡直接呼叫與 Program.cs 相同的 Configure 方法（不是重寫一份），
// 這行被拿掉或改回 true 時本測試會翻紅。
public class JwtClaimMappingTests
{
    private const string SigningKey = "unit-test-signing-key-at-least-32-bytes-long";
    private const string Issuer = "KnowledgeHub";
    private const string Audience = "KnowledgeHub";

    [Fact]
    public async Task 驗證後sub與department可用字面claim名取到值()
    {
        var jwt = new TokenService(SigningKey, Issuer, Audience).CreateToken("it-user", "IT");

        var options = new JwtBearerOptions();
        JwtBearerConfigurator.Configure(options, SigningKey, Issuer, Audience);

        var handler = new JsonWebTokenHandler { MapInboundClaims = options.MapInboundClaims };
        var result = await handler.ValidateTokenAsync(jwt, options.TokenValidationParameters);

        Assert.True(result.IsValid, result.Exception?.ToString());
        Assert.Equal("it-user", result.ClaimsIdentity!.FindFirst("sub")?.Value);
        Assert.Equal("IT", result.ClaimsIdentity!.FindFirst("department")?.Value);
    }
}
