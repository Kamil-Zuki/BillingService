using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using BillingService.Options;
using BillingService.Providers.Models;
using Microsoft.Extensions.Options;

namespace BillingService.Providers.YooKassa;

public class YooKassaPaymentProvider : IPaymentProvider
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<YooKassaPaymentProvider> _logger;
    private readonly YooKassaOptions _options;

    public string ProviderCode => "yookassa";

    public YooKassaPaymentProvider(
        HttpClient httpClient,
        IOptions<YooKassaOptions> options,
        ILogger<YooKassaPaymentProvider> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _options = options.Value;

        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.ShopId}:{_options.SecretKey}"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        _httpClient.BaseAddress = new Uri("https://api.yookassa.ru/v3/");
    }

    public async Task<CheckoutSessionResult> CreateCheckoutAsync(CheckoutRequest request, CancellationToken cancellationToken = default)
    {
        var body = new JsonObject
        {
            ["amount"] = new JsonObject
            {
                ["value"] = FormatAmount(request.Price),
                ["currency"] = request.Currency
            },
            ["capture"] = true,
            ["confirmation"] = new JsonObject
            {
                ["type"] = "redirect",
                ["return_url"] = request.ReturnUrl ?? _options.ReturnUrl
            },
            ["save_payment_method"] = true,
            ["merchant_customer_id"] = request.UserId.ToString("N"),
            ["metadata"] = new JsonObject
            {
                ["planCode"] = request.PlanCode,
                ["customerId"] = request.CustomerId.ToString("N")
            },
            ["description"] = $"Подписка {request.PlanCode}"
        };

        var content = CreateJsonContent(body);
        AddIdempotenceKey(content);

        var response = await _httpClient.PostAsync("payments", content, cancellationToken);
        var responseJson = await EnsureSuccessAndParseAsync(response, cancellationToken);

        var paymentId = responseJson!["id"]!.GetValue<string>();
        var confirmationUrl = responseJson["confirmation"]?["confirmation_url"]?.GetValue<string>() ?? string.Empty;

        return new CheckoutSessionResult(confirmationUrl, paymentId);
    }

    public async Task<RecurringPaymentResult> CreateRecurringPaymentAsync(RecurringPaymentRequest request, CancellationToken cancellationToken = default)
    {
        var body = new JsonObject
        {
            ["amount"] = new JsonObject
            {
                ["value"] = FormatAmount(request.Amount),
                ["currency"] = request.Currency
            },
            ["capture"] = true,
            ["payment_method_id"] = request.ProviderPaymentMethodId,
            ["description"] = $"Продление подписки {request.PlanCode}"
        };

        var content = CreateJsonContent(body);
        AddIdempotenceKey(content);

        var response = await _httpClient.PostAsync("payments", content, cancellationToken);
        var responseJson = await EnsureSuccessAndParseAsync(response, cancellationToken);

        var paymentId = responseJson!["id"]!.GetValue<string>();
        var status = responseJson["status"]!.GetValue<string>();
        var paidAt = ParseNullableDateTime(responseJson["paid_at"]);

        return new RecurringPaymentResult(paymentId, status, paidAt);
    }

    public async Task<PaymentStatusResult> GetPaymentStatusAsync(string providerPaymentId, CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync($"payments/{providerPaymentId}", cancellationToken);
        var responseJson = await EnsureSuccessAndParseAsync(response, cancellationToken);

        var status = responseJson!["status"]!.GetValue<string>();
        var paidAt = ParseNullableDateTime(responseJson["paid_at"]);

        return new PaymentStatusResult(status, paidAt);
    }

    public Task<WebhookHandleResult> HandleWebhookAsync(WebhookPayload payload, CancellationToken cancellationToken = default)
    {
        using var document = JsonDocument.Parse(payload.Body);
        var root = document.RootElement;

        var eventType = root.GetProperty("event").GetString() ?? string.Empty;
        _logger.LogInformation("YooKassa webhook event: {EventType}", eventType);

        if (!root.TryGetProperty("object", out var obj))
        {
            return Task.FromResult(WebhookHandleResult.Empty);
        }

        var events = new List<DomainEvent>();

        switch (eventType)
        {
            case "payment.succeeded":
                events.Add(MapPaymentSucceeded(obj));
                if (obj.TryGetProperty("payment_method", out var paymentMethod))
                {
                    events.Add(MapPaymentMethodSaved(paymentMethod, obj.GetProperty("metadata").GetProperty("customerId").GetString() ?? string.Empty));
                }
                break;

            case "payment.canceled":
                events.Add(MapPaymentFailed(obj));
                break;

            case "refund.succeeded":
                // refunds are out of scope for v1
                break;
        }

        return Task.FromResult(new WebhookHandleResult(events));
    }

    public bool VerifyWebhookSignature(WebhookPayload payload, string? secret)
    {
        if (string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(payload.SignatureHeader))
        {
            return true;
        }

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload.Body));
        var expected = Convert.ToHexString(hash);
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(payload.SignatureHeader));
    }

    private static DomainEvent MapPaymentSucceeded(JsonElement payment)
    {
        var metadata = payment.GetProperty("metadata");
        var planCode = metadata.TryGetProperty("planCode", out var planCodeProp)
            ? planCodeProp.GetString()
            : null;

        var amount = ParseAmount(payment.GetProperty("amount"));
        var paidAt = ParseNullableDateTime(payment.GetProperty("paid_at")) ?? DateTime.UtcNow;
        var paymentMethod = payment.GetProperty("payment_method");
        var card = paymentMethod.TryGetProperty("card", out var cardProp) ? cardProp : default;

        return new PaymentSucceededEvent(
            payment.GetProperty("id").GetString()!,
            null,
            metadata.GetProperty("customerId").GetString() ?? payment.GetProperty("merchant_customer_id").GetString() ?? string.Empty,
            paymentMethod.GetProperty("id").GetString()!,
            payment.GetProperty("id").GetString()!,
            amount,
            payment.GetProperty("amount").GetProperty("currency").GetString() ?? "RUB",
            paidAt,
            planCode,
            card.TryGetProperty("brand", out var brand) ? brand.GetString() : null,
            card.TryGetProperty("last4", out var last4) ? last4.GetString() : null,
            card.TryGetProperty("expiry_month", out var expMonth) && expMonth.ValueKind == JsonValueKind.String
                ? int.TryParse(expMonth.GetString(), out var m) ? m : null
                : card.TryGetProperty("expiry_month", out expMonth) && expMonth.ValueKind == JsonValueKind.Number
                    ? expMonth.GetInt32()
                    : null,
            card.TryGetProperty("expiry_year", out var expYear) && expYear.ValueKind == JsonValueKind.String
                ? int.TryParse(expYear.GetString(), out var y) ? y : null
                : card.TryGetProperty("expiry_year", out expYear) && expYear.ValueKind == JsonValueKind.Number
                    ? expYear.GetInt32()
                    : null);
    }

    private static DomainEvent MapPaymentFailed(JsonElement payment)
    {
        return new PaymentFailedEvent(
            payment.GetProperty("id").GetString()!,
            null,
            payment.TryGetProperty("merchant_customer_id", out var customerId)
                ? customerId.GetString() ?? string.Empty
                : string.Empty,
            payment.TryGetProperty("payment_method", out var pm) && pm.TryGetProperty("id", out var pmId)
                ? pmId.GetString()
                : null,
            DateTime.UtcNow);
    }

    private static DomainEvent MapPaymentMethodSaved(JsonElement paymentMethod, string customerId)
    {
        var card = paymentMethod.TryGetProperty("card", out var cardProp) ? cardProp : default;
        return new PaymentMethodSavedEvent(
            paymentMethod.GetProperty("id").GetString()!,
            customerId,
            paymentMethod.GetProperty("type").GetString() ?? "unknown",
            card.TryGetProperty("brand", out var brand) ? brand.GetString() : null,
            card.TryGetProperty("last4", out var last4) ? last4.GetString() : null,
            card.TryGetProperty("expiry_month", out var expMonth) && expMonth.ValueKind == JsonValueKind.Number
                ? expMonth.GetInt32()
                : null,
            card.TryGetProperty("expiry_year", out var expYear) && expYear.ValueKind == JsonValueKind.Number
                ? expYear.GetInt32()
                : null);
    }

    private static int ParseAmount(JsonElement amountElement)
    {
        var valueString = amountElement.GetProperty("value").GetString()
            ?? amountElement.GetProperty("value").GetDecimal().ToString(CultureInfo.InvariantCulture);

        if (decimal.TryParse(valueString, NumberStyles.Any, CultureInfo.InvariantCulture, out var value))
        {
            return (int)(value * 100);
        }

        return 0;
    }

    private static string FormatAmount(int amountInCents)
    {
        return (amountInCents / 100m).ToString("F2", CultureInfo.InvariantCulture);
    }

    private static StringContent CreateJsonContent(JsonObject body)
    {
        return new StringContent(
            body.ToJsonString(new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }),
            Encoding.UTF8,
            "application/json");
    }

    private static void AddIdempotenceKey(HttpContent content)
    {
        content.Headers.Add("Idempotence-Key", Guid.NewGuid().ToString("N"));
    }

    private async Task<JsonObject> EnsureSuccessAndParseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("YooKassa API error {StatusCode}: {Body}", (int)response.StatusCode, body);
            response.EnsureSuccessStatusCode();
        }

        return JsonNode.Parse(body)!.AsObject();
    }

    private static DateTime? ParseNullableDateTime(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Null || element.ValueKind == JsonValueKind.Undefined)
        {
            return null;
        }

        if (DateTime.TryParse(element.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var dt))
        {
            return dt;
        }

        return null;
    }

    private static DateTime? ParseNullableDateTime(JsonNode? node)
    {
        if (node is null)
        {
            return null;
        }

        var value = node.GetValue<string>();
        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var dt))
        {
            return dt;
        }

        return null;
    }
}
