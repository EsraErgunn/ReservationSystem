using ReservationSystem.Domain.Common;
using ReservationSystem.Domain.Enums;

namespace ReservationSystem.Domain.Entities;

public class User : Entity
{
    public string Email { get; private set; } = null!;
    public string PasswordHash { get; private set; } = null!;
    public string FullName { get; private set; } = null!;
    public UserRole Role { get; private set; }
    public DateTime CreatedAt { get; private set; }

    private User() { }   // EF Core için

    public User(string email, string passwordHash, string fullName, DateTime utcNow)
    {
        Email = email.Trim().ToLowerInvariant();
        PasswordHash = passwordHash;
        FullName = fullName;
        Role = UserRole.User;
        CreatedAt = utcNow;
    }
}
