using System.IdentityModel.Tokens.Jwt;
using KnowledgeHub.Api.Auth;

public class TokenServiceTests
{
    private static TokenService NewService() =>
        new(signingKey: new string('k', 32), issuer: "KnowledgeHub", audience: "KnowledgeHub");

    [Fact]
    public void Token含department與sub_claim()
    {
        var token = NewService().CreateToken(username: "it-user", department: "IT");
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);

        Assert.Equal("IT", jwt.Claims.Single(c => c.Type == "department").Value);
        Assert.Equal("it-user", jwt.Claims.Single(c => c.Type == "sub").Value);
        Assert.Equal("KnowledgeHub", jwt.Issuer);
    }
}
