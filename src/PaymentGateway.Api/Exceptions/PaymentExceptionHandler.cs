using FluentValidation;
using FluentValidation.Results;

using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

using PaymentGateway.Api.Models.Responses;

namespace PaymentGateway.Api.Exceptions;

public class PaymentExceptionHandler : IExceptionHandler
{
    private readonly IProblemDetailsService _problemDetailsService;
    private readonly JsonOptions _jsonOptions;

    public PaymentExceptionHandler(IProblemDetailsService problemDetailsService, IOptions<JsonOptions> jsonOptions)
    {
        _problemDetailsService = problemDetailsService;
        _jsonOptions = jsonOptions.Value;
    }

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        switch (exception)
        {
            case ValidationException validationException:
                httpContext.Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
                var rejected = new RejectedPaymentResponse { Errors = new ValidationResult(validationException.Errors).ToDictionary() };
                await httpContext.Response.WriteAsJsonAsync(rejected, _jsonOptions.JsonSerializerOptions, cancellationToken);
                return true;

            case AcquiringBankException { OutcomeUnknown: true }:
                return await WriteProblemAsync(
                    httpContext,
                    exception,
                    StatusCodes.Status504GatewayTimeout,
                    "Acquiring bank did not respond in time",
                    "The payment outcome is unknown. Do not retry this payment without first confirming its status.");

            case AcquiringBankException:
                return await WriteProblemAsync(
                    httpContext,
                    exception,
                    StatusCodes.Status502BadGateway,
                    "Acquiring bank unavailable",
                    "The payment was not processed. It is safe to retry.");

            default:
                return false;
        }
    }

    private ValueTask<bool> WriteProblemAsync(HttpContext httpContext, Exception exception, int statusCode, string title, string detail)
    {
        httpContext.Response.StatusCode = statusCode;

        return _problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails { Status = statusCode, Title = title, Detail = detail }
        });
    }
}