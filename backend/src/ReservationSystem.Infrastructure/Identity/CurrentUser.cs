using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using ReservationSystem.Application.Abstractions;
using ReservationSystem.Domain.Enums;

namespace ReservationSystem.Infrastructure.Identity;

/// <summary>
/// NFR-08: yetkilendirme istemciye bırakılmaz — kimlik her istekte JWT
/// claim'lerinden sunucuda okunur.
/// </summary>
public class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public Guid? UserId =>
        Guid.TryParse(Find(ClaimTypes.NameIdentifier) ?? Find("sub"), out var id) ? id : null;

    public bool IsAdmin =>
        string.Equals(Find(ClaimTypes.Role) ?? Find("role"),
            nameof(UserRole.Admin), StringComparison.OrdinalIgnoreCase);

    private string? Find(string claimType) =>
        accessor.HttpContext?.User.FindFirst(claimType)?.Value;
}
