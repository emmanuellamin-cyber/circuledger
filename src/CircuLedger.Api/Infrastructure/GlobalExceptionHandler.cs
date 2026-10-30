using CircuLedger.Application.Common;
using CircuLedger.Domain.Exceptions;
using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CircuLedger.Api.Infrastructure;

/// <summary>Maps exceptions to RFC 7807 ProblemDetails. Unexpected errors never leak internals.</summary>
public sealed class GlobalExceptionHandler(IProblemDetailsService problemDetails, ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        ProblemDetails problem = exception switch
        {
            ValidationException ve => new ValidationProblemDetails(
                ve.Errors.Any()
                    ? ve.Errors.GroupBy(e => e.PropertyName).ToDictionary(g => string.IsNullOrEmpty(g.Key) ? "request" : g.Key, g => g.Select(e => e.ErrorMessage).ToArray())
                    : new Dictionary<string, string[]> { ["request"] = [ve.Message] })
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "One or more validation errors occurred."
            },
            NotFoundException => Problem(StatusCodes.Status404NotFound, "Resource not found", exception.Message, "not_found"),
            InsufficientFundsException ife => Problem(StatusCodes.Status409Conflict, "Insufficient funds", ife.Message, ife.Code),
            ConflictException ce => Problem(StatusCodes.Status409Conflict, "Conflict", ce.Message, ce.Code),
            DomainException de => Problem(StatusCodes.Status422UnprocessableEntity, "Business rule violated", de.Message, de.Code),
            ForbiddenAccessException => Problem(StatusCodes.Status403Forbidden, "Forbidden", exception.Message, "forbidden"),
            UnauthorizedAccessException => Problem(StatusCodes.Status401Unauthorized, "Unauthorized", exception.Message, "unauthorized"),
            DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } } =>
                Problem(StatusCodes.Status409Conflict, "Conflict", "The record conflicts with an existing one.", "duplicate"),
            BadHttpRequestException bad => Problem(bad.StatusCode, "Bad request", bad.Message, "bad_request"),
            OperationCanceledException when context.RequestAborted.IsCancellationRequested =>
                Problem(499, "Client closed request", null, "cancelled"),
            _ => Problem(StatusCodes.Status500InternalServerError, "An unexpected error occurred",
                "The error has been logged. Quote the traceId when contacting support.", "server_error")
        };

        if (problem.Status >= 500)
            logger.LogError(exception, "Unhandled exception on {Method} {Path}", context.Request.Method, context.Request.Path);
        else
            logger.LogInformation("Request failed with {Status}: {Type}", problem.Status, exception.GetType().Name);

        context.Response.StatusCode = problem.Status ?? 500;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = problem,
            Exception = exception
        });
    }

    private static ProblemDetails Problem(int status, string title, string? detail, string code) => new()
    {
        Status = status,
        Title = title,
        Detail = detail,
        Type = $"https://httpstatuses.io/{status}",
        Extensions = { ["code"] = code }
    };
}
