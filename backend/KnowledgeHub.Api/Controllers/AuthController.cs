using KnowledgeHub.Api.Auth;
using Microsoft.AspNetCore.Mvc;

namespace KnowledgeHub.Api.Controllers;

public record LoginRequest(string Username, string Password);
public record LoginResponse(string Token);

[ApiController]
[Route("api/auth")]
public class AuthController(IEnumerable<SeedUser> users, TokenService tokens) : ControllerBase
{
    [HttpPost("login")]
    public IActionResult Login(LoginRequest request)
    {
        var user = users.FirstOrDefault(u =>
            u.Username == request.Username && u.Password == request.Password);
        return user is null
            ? Unauthorized()
            : Ok(new LoginResponse(tokens.CreateToken(user.Username, user.Department)));
    }
}
