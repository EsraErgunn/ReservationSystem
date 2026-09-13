using ReservationSystem.Domain.Entities;

namespace ReservationSystem.Application.Abstractions;

/// <summary>
/// auth.md §2. <c>System.IdentityModel.Tokens.Jwt</c> Application'a sızmamalı —
/// bu port yalnızca "kullanıcıyı temsil eden bir erişim belirteci üret" der.
/// </summary>
/// <remarks>
/// <c>Guid userId</c> değil <see cref="User"/> alıyor: token'a rol, e-posta ve ad
/// claim'leri de girecek. <c>User</c> zaten Domain tipi, Application onu biliyor.
/// </remarks>
public interface ITokenService
{
    string CreateAccessToken(User user);
}
