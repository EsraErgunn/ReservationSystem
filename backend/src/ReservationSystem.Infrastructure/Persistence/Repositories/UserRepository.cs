using Microsoft.EntityFrameworkCore;
using ReservationSystem.Application.Abstractions;
using ReservationSystem.Domain.Entities;

namespace ReservationSystem.Infrastructure.Persistence.Repositories;

public class UserRepository(AppDbContext context) : IUserRepository
{
    public Task<User?> GetByIdAsync(Guid id, CancellationToken ct) =>
        context.Users.FirstOrDefaultAsync(u => u.Id == id, ct);

    // Handler e-postayı ToLowerInvariant() ile normalize ediyor, entity ctor'u da öyle
    // kaydediyor — bu yüzden düz eşitlik ux_users_email (lower(email)) index'ine düşer.
    public Task<bool> ExistsByEmailAsync(string email, CancellationToken ct) =>
        context.Users.AnyAsync(u => u.Email == email, ct);

    public Task<User?> GetByEmailAsync(string email, CancellationToken ct) =>
        context.Users.FirstOrDefaultAsync(u => u.Email == email, ct);

    public void Add(User user) => context.Users.Add(user);
}
