using System.IdentityModel.Tokens.Jwt;
using KnowledgeHub.Api.Auth;
using KnowledgeHub.Api.Controllers;
using Microsoft.AspNetCore.Mvc;

public class AuthControllerTests
{
    private static readonly SeedUser[] Users =
        [new() { Username = "it-user", Password = "demo-it-2026", Department = "IT" }];

    private static AuthController NewController() =>
        new(Users, new TokenService(new string('k', 32), "KnowledgeHub", "KnowledgeHub"));

    [Fact]
    public void 帳密正確回token()
    {
        var result = NewController().Login(new LoginRequest("it-user", "demo-it-2026"));
        var ok = Assert.IsType<OkObjectResult>(result);
        var token = Assert.IsType<LoginResponse>(ok.Value).Token;
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        Assert.Equal("IT", jwt.Claims.Single(c => c.Type == "department").Value);
    }

    [Theory]
    [InlineData("it-user", "wrong")]
    [InlineData("nobody", "demo-it-2026")]
    public void 帳密錯誤回401(string user, string pass)
        => Assert.IsType<UnauthorizedResult>(NewController().Login(new LoginRequest(user, pass)));
}
