using Microsoft.EntityFrameworkCore;
using ReservationSystem.Application.Abstractions;
using ReservationSystem.Domain.Entities;

namespace ReservationSystem.Infrastructure.Persistence.Repositories;

public class UserRepository(AppDbContext context) : IUserRepository
{
    public Task<User?> GetByIdAsync(Guid id, CancellationToken ct) =>
        context.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
}
