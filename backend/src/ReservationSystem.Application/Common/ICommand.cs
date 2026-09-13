namespace ReservationSystem.Application.Common;

/// <summary>
/// Durum değiştiren bir kullanım senaryosu. MediatR yerine elle yazılmış minimal
/// sözleşme (bkz. application-katmani.md §2) — controller handler'ı doğrudan enjekte eder.
/// </summary>
public interface ICommand<TResult>;

public interface ICommandHandler<TCommand, TResult>
    where TCommand : ICommand<TResult>
{
    Task<TResult> HandleAsync(TCommand command, CancellationToken ct);
}
