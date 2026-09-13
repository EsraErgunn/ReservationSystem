using System.Diagnostics;
using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using ReservationSystem.Application.Common;
using ReservationSystem.Domain.Common;

namespace ReservationSystem.Api.Handlers;

/// <summary>
/// Tek çıkış noktası: Application/Domain istisnaları burada HTTP durum koduna ve
/// <c>ProblemDetails</c> gövdesine çevrilir. Controller'larda try/catch yok.
/// </summary>
public class AppExceptionHandler(ILogger<AppExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext context, Exception exception, CancellationToken ct)
    {
        var (status, code, detail) = Map(exception);

        if (status >= 500)
            logger.LogError(exception, "İşlenmeyen hata: {Code}", code);
        else
            logger.LogWarning("İş hatası: {Code} — {Detail}", code, detail);

        var problem = new ProblemDetails
        {
            Status = status,
            Title = code,
            Detail = detail,
            Instance = context.Request.Path
        };

        // NFR-09: kullanıcı "hata aldım" dediğinde loglardan tam o isteği bulabilmek için.
        problem.Extensions["traceId"] = Activity.Current?.Id ?? context.TraceIdentifier;

        context.Response.StatusCode = status;
        await context.Response.WriteAsJsonAsync(problem, ct);
        return true;
    }

    private static (int Status, string Code, string Detail) Map(Exception ex) => ex switch
    {
        NotFoundAppException e         => (404, e.Code, e.Message),
        ConcurrencyConflictException e => (409, e.Code, e.Message),
        UniqueConstraintException e    => (409, e.Code, e.Message),
        ConflictAppException e         => (409, e.Code, e.Message),
        ForbiddenAppException e        => (403, e.Code, e.Message),
        UnauthorizedAppException e     => (401, e.Code, e.Message),
        PaymentAppException e          => (422, e.Code, e.Message),
        DomainException e              => (400, e.Code, e.Message),

        // auth.md §8: RegisterValidator handler içinde çalışıyor (bağlanan model
        // RegisterRequest, doğrulanan tip RegisterCommand — otomatik doğrulama
        // devreye girmez). Fırlattığı istisna burada 400'e çevriliyor.
        ValidationException e           => (400, "validation_error",
            string.Join(" ", e.Errors.Select(f => f.ErrorMessage))),

        // Bilinmeyen hata — iç detay SIZDIRILMAZ (NFR-08). Bağlantı dizesi, dosya
        // yolu gibi bilgiler ex.Message içinde olabilir; onlar yalnızca log'a gider.
        _ => (500, "internal_error", "Beklenmeyen bir hata oluştu.")
    };
}
