using System.Text.Json;
using System.Text.Json.Serialization;

using FluentValidation;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;

using PaymentGateway.Api.Configuration;
using PaymentGateway.Api.Models.Requests;
using PaymentGateway.Api.Models.Responses;
using PaymentGateway.Api.Services;
using PaymentGateway.Api.Validation;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        options.JsonSerializerOptions.DictionaryKeyPolicy = JsonNamingPolicy.CamelCase;
    })
    .ConfigureApiBehaviorOptions(options => options.InvalidModelStateResponseFactory = context =>
        new UnprocessableEntityObjectResult(RejectedPaymentResponse.FromModelState(context.ModelState)));
builder.Services.AddProblemDetails();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IValidator<PostPaymentRequest>, PostPaymentRequestValidator>();
builder.Services.AddSingleton<IPaymentsRepository, PaymentsRepository>();
builder.Services.AddScoped<IPaymentService, PaymentService>();

builder.Services.AddOptions<BankOptions>()
    .BindConfiguration(BankOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddHttpClient<IBankClient, BankClient>((serviceProvider, client) =>
        client.BaseAddress = serviceProvider.GetRequiredService<IOptions<BankOptions>>().Value.BaseAddress)
    .AddStandardResilienceHandler()
    .Configure((HttpStandardResilienceOptions options, IServiceProvider serviceProvider) =>
    {
        var timeout = serviceProvider.GetRequiredService<IOptions<BankOptions>>().Value.Timeout;

        options.Retry.DisableForUnsafeHttpMethods();
        options.AttemptTimeout.Timeout = timeout;
        options.TotalRequestTimeout.Timeout = timeout;
    });

var app = builder.Build();

app.UseExceptionHandler();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();