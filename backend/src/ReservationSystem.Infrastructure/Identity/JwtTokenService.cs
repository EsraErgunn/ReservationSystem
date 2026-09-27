using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using ReservationSystem.Application.Abstractions;
using ReservationSystem.Domain.Entities;

namespace ReservationSystem.Infrastructure.Identity;

/// <summary>
/// auth.md §6. Token'a parola hash'i, kimlik numarası, telefon gibi hiçbir şey
/// konulmaz: JWT <b>imzalıdır ama şifreli değildir</b>, içeriğini herkes okuyabilir.
/// Yalnızca kimlik ve yetki için gereken minimum bilgi girer.
/// </summary>
public class JwtTokenService(IOptions<JwtOptions> options, TimeProvider clock) : ITokenService
{
    public string CreateAccessToken(User user)
    {
        var opt = options.Value;

        // ClaimTypes.Role kullanımı şart: Api'deki CurrentUser.IsAdmin bu claim'i okuyor.
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Name, user.FullName),
            new(ClaimTypes.Role, user.Role.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.CreateVersion7().ToString())
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(opt.Key));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: opt.Issuer,
            audience: opt.Audience,
            claims: claims,
            expires: clock.GetUtcNow().UtcDateTime.AddMinutes(opt.ExpiryMinutes),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
