using FluentValidation;
using FluentValidation.Results;

using Microsoft.AspNetCore.Mvc;

using PaymentGateway.Api.Exceptions;
using PaymentGateway.Api.Models.Requests;
using PaymentGateway.Api.Models.Responses;
using PaymentGateway.Api.Services;

namespace PaymentGateway.Api.Controllers;

[Route("api/[controller]")]
[ApiController]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class PaymentsController : ControllerBase
{
    private readonly IPaymentService _paymentService;

    public PaymentsController(IPaymentService paymentService)
    {
        _paymentService = paymentService;
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType<PaymentResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public ActionResult<PaymentResponse> GetPayment(Guid id)
    {
        var payment = _paymentService.GetPayment(id);

        if (payment is null)
        {
            return NotFound();
        }

        return PaymentResponse.FromPayment(payment);
    }

    [HttpPost]
    [ProducesResponseType<PaymentResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<RejectedPaymentResponse>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status502BadGateway)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status504GatewayTimeout)]
    public async Task<ActionResult<PaymentResponse>> PostPayment(PostPaymentRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var payment = await _paymentService.ProcessPaymentAsync(request, cancellationToken);

            return CreatedAtAction(nameof(GetPayment), new { id = payment.Id }, PaymentResponse.FromPayment(payment));
        }
        catch (ValidationException exception)
        {
            return UnprocessableEntity(new RejectedPaymentResponse { Errors = new ValidationResult(exception.Errors).ToDictionary() });
        }
        catch (AcquiringBankException exception) when (exception.OutcomeUnknown)
        {
            return Problem(
                statusCode: StatusCodes.Status504GatewayTimeout,
                title: "Acquiring bank did not respond in time",
                detail: "The payment outcome is unknown. Do not retry this payment without first confirming its status.");
        }
        catch (AcquiringBankException)
        {
            return Problem(
                statusCode: StatusCodes.Status502BadGateway,
                title: "Acquiring bank unavailable",
                detail: "The payment was not processed. It is safe to retry.");
        }
    }
}