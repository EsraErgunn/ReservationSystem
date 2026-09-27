using FluentValidation;
using ReservationSystem.Application.Abstractions;
using ReservationSystem.Application.Common;
using ReservationSystem.Domain.Entities;

namespace ReservationSystem.Application.Events;

/// <summary>
/// FR-12: admin etkinlik tanımlar. Mekânın her koltuğu için bir EventSeat üretilir;
/// fiyat varsayılan fiyattır, <see cref="RowPrices"/> ile sıra bazında ezilebilir.
/// </summary>
public record CreateEventCommand(
    Guid VenueId,
    string Title,
    string? Description,
    DateTime EventDate,
    DateTime SalesStartAt,
    DateTime SalesEndAt,
    decimal DefaultPrice,
    IReadOnlyList<RowPriceInput>? RowPrices) : ICommand<EventCreatedDto>;

public record RowPriceInput(string RowLabel, decimal Price);

public record EventCreatedDto(Guid Id, string Title, int SeatCount);

public class CreateEventValidator : AbstractValidator<CreateEventCommand>
{
    public CreateEventValidator()
    {
        RuleFor(x => x.VenueId).NotEmpty();
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(2000);
        RuleFor(x => x.DefaultPrice).GreaterThan(0).LessThan(100_000);

        // Tarih penceresi kuralları Domain'de (Event ctor) — burada tekrar edilmiyor.

        RuleForEach(x => x.RowPrices).ChildRules(row =>
        {
            row.RuleFor(r => r.RowLabel).NotEmpty().MaximumLength(8);
            row.RuleFor(r => r.Price).GreaterThan(0).LessThan(100_000);
        });
    }
}

public class CreateEventHandler(
    IVenueRepository venues,
    IEventRepository events,
    IUnitOfWork uow,
    ICurrentUser currentUser,
    IEventCatalogCache catalogCache,
    IValidator<CreateEventCommand> validator)
    : ICommandHandler<CreateEventCommand, EventCreatedDto>
{
    public async Task<EventCreatedDto> HandleAsync(CreateEventCommand command, CancellationToken ct)
    {
        // BR-16
        if (currentUser.UserId is null) throw new UnauthorizedAppException();
        if (!currentUser.IsAdmin) throw new ForbiddenAppException();

        await validator.ValidateAndThrowAsync(command, ct);

        _ = await venues.GetByIdAsync(command.VenueId, ct)
            ?? throw new NotFoundAppException("venue", command.VenueId);

        var venueSeats = await venues.GetSeatsAsync(command.VenueId, ct);

        var @event = new Event(
            command.VenueId,
            command.Title,
            command.EventDate.AsUtc(),
            command.SalesStartAt.AsUtc(),
            command.SalesEndAt.AsUtc(),
            command.Description);

        var rowPrices = (command.RowPrices ?? [])
            .GroupBy(r => r.RowLabel.Trim().ToUpperInvariant())
            .ToDictionary(g => g.Key, g => g.Last().Price);

        var eventSeats = @event.CreateSeats(
            venueSeats,
            seat => rowPrices.GetValueOrDefault(seat.RowLabel, command.DefaultPrice));

        events.Add(@event);
        await uow.SaveChangesAsync(ct);

        // Liste cache'i yeni etkinliği hemen göstersin
        await catalogCache.InvalidateAsync(ct);

        return new EventCreatedDto(@event.Id, @event.Title, eventSeats.Count);
    }
}
