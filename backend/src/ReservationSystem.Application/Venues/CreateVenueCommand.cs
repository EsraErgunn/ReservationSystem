using FluentValidation;
using ReservationSystem.Application.Abstractions;
using ReservationSystem.Application.Common;
using ReservationSystem.Domain.Entities;

namespace ReservationSystem.Application.Venues;

/// <summary>FR-12: admin mekân ve koltuk düzeni tanımlar.</summary>
public record CreateVenueCommand(
    string Name,
    string Address,
    string City,
    IReadOnlyList<VenueRowInput> Rows) : ICommand<VenueDto>;

public record VenueRowInput(string RowLabel, int SeatCount);

public record VenueDto(Guid Id, string Name, string Address, string City, int SeatCount);

public class CreateVenueValidator : AbstractValidator<CreateVenueCommand>
{
    /// <summary>Tek mekân için üst sınır — yanlışlıkla on binlerce satır üretilmesin.</summary>
    public const int MaxSeatsPerVenue = 5000;

    public CreateVenueValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(128);
        RuleFor(x => x.Address).NotEmpty().MaximumLength(512);
        RuleFor(x => x.City).NotEmpty().MaximumLength(64);

        RuleFor(x => x.Rows).NotEmpty().WithMessage("En az bir sıra tanımlanmalı.");

        RuleForEach(x => x.Rows).ChildRules(row =>
        {
            // seats.row_label varchar(8)
            row.RuleFor(r => r.RowLabel).NotEmpty().MaximumLength(8);
            row.RuleFor(r => r.SeatCount).InclusiveBetween(1, 200);
        });

        RuleFor(x => x.Rows)
            .Must(rows => rows.Sum(r => r.SeatCount) <= MaxSeatsPerVenue)
            .WithMessage($"Bir mekânda en fazla {MaxSeatsPerVenue} koltuk olabilir.")
            .When(x => x.Rows is { Count: > 0 });
    }
}

public class CreateVenueHandler(
    IVenueRepository venues,
    IUnitOfWork uow,
    ICurrentUser currentUser,
    IValidator<CreateVenueCommand> validator)
    : ICommandHandler<CreateVenueCommand, VenueDto>
{
    public async Task<VenueDto> HandleAsync(CreateVenueCommand command, CancellationToken ct)
    {
        // BR-16 — controller'daki [Authorize(Roles)] ile çift kontrol: handler başka
        // bir giriş noktasından çağrılsa da kural geçerli kalsın.
        if (currentUser.UserId is null) throw new UnauthorizedAppException();
        if (!currentUser.IsAdmin) throw new ForbiddenAppException();

        await validator.ValidateAndThrowAsync(command, ct);

        var venue = new Venue(command.Name, command.Address, command.City);
        var seats = venue.CreateLayout(
            command.Rows.Select(r => (r.RowLabel, r.SeatCount)).ToList());

        venues.Add(venue, seats);
        await uow.SaveChangesAsync(ct);

        return new VenueDto(venue.Id, venue.Name, venue.Address, venue.City, seats.Count);
    }
}
