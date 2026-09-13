namespace ReservationSystem.Application.Common;

/// <summary>
/// Okuma tarafı. Sorgular domain'den geçmez; doğrudan okuma modeli döner
/// (CQRS okuma tarafı, bkz. application-katmani.md §9).
/// </summary>
public interface IQuery<TResult>;

public interface IQueryHandler<TQuery, TResult>
    where TQuery : IQuery<TResult>
{
    Task<TResult> HandleAsync(TQuery query, CancellationToken ct);
}
