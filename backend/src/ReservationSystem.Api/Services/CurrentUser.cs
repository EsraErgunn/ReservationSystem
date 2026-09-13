using System.Security.Claims;
using ReservationSystem.Application.Abstractions;
using ReservationSystem.Domain.Enums;

namespace ReservationSystem.Api.Services;

/// <summary>
/// NFR-08: yetkilendirme istemciye bırakılmaz — kimlik her istekte JWT claim'lerinden
/// sunucuda okunur.
/// <para>
/// Neden Api'de, Infrastructure'da değil: <c>HttpContext</c> bir web kavramı.
/// Infrastructure'a konsaydı, HTTP isteği olmayan arka plan servisleri de bu sınıfa
/// bağımlı hale gelme riski taşırdı (api-katmani.md §4).
/// </para>
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
