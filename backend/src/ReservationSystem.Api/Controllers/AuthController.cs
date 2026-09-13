using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ReservationSystem.Application.Auth;
using ReservationSystem.Application.Common;

namespace ReservationSystem.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(
    ICommandHandler<RegisterCommand, AuthResult> register,
    ICommandHandler<LoginCommand, AuthResult> login)
    : ControllerBase
{
    [HttpPost("register")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [ProducesResponseType<AuthResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Register(RegisterRequest request, CancellationToken ct)
        => Ok(await register.HandleAsync(
            new RegisterCommand(request.Email, request.Password, request.FullName), ct));

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [ProducesResponseType<AuthResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken ct)
        => Ok(await login.HandleAsync(
            new LoginCommand(request.Email, request.Password), ct));

    [HttpGet("me")]
    [Authorize]
    public IActionResult Me()
        => Ok(new
        {
            Id = User.FindFirstValue(ClaimTypes.NameIdentifier),
            Email = User.FindFirstValue(ClaimTypes.Email),
            Name = User.FindFirstValue(ClaimTypes.Name),
            Role = User.FindFirstValue(ClaimTypes.Role)
        });
}

public record RegisterRequest(string Email, string Password, string FullName);

public record LoginRequest(string Email, string Password);
