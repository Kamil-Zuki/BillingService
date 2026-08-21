using BillingService.Data;
using BillingService.Grpc;
using BillingService.Options;
using BillingService.Providers;
using BillingService.Providers.YooKassa;
using BillingService.Services;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

var builder = WebApplication.CreateBuilder(args);

string? connection = builder.Configuration.GetConnectionString("DefaultConnection");

var dataSourceBuilder = new Npgsql.NpgsqlDataSourceBuilder(connection);
dataSourceBuilder.EnableDynamicJson();
var dataSource = dataSourceBuilder.Build();

builder.Services.AddDbContext<BillingServiceContext>(options =>
    options.UseNpgsql(dataSource, npgsqlOptions =>
    {
        npgsqlOptions.EnableRetryOnFailure(
            maxRetryCount: 5,
            maxRetryDelay: TimeSpan.FromSeconds(10),
            errorCodesToAdd: null);
    }));

builder.Services.Configure<BillingOptions>(
    builder.Configuration.GetSection(BillingOptions.SectionName));
builder.Services.Configure<YooKassaOptions>(
    builder.Configuration.GetSection(YooKassaOptions.SectionName));

builder.Services.AddHttpClient<YooKassaPaymentProvider>();

builder.Services.AddSingleton<IPaymentProvider, YooKassaPaymentProvider>();
builder.Services.AddSingleton<IPaymentProvider, MockPaymentProvider>();
builder.Services.AddSingleton<IPaymentProviderFactory, PaymentProviderFactory>();

builder.Services.AddScoped<IAccessService, AccessService>();
builder.Services.AddScoped<IEntitlementService, EntitlementService>();
builder.Services.AddScoped<ISubscriptionService, SubscriptionService>();
builder.Services.AddScoped<IInvoiceService, InvoiceService>();
builder.Services.AddScoped<IWebhookOrchestrator, WebhookOrchestrator>();

builder.Services.AddHostedService<RenewalWorker>();

builder.WebHost.ConfigureKestrel(options =>
{
    var inContainer = string.Equals(Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"), "true", StringComparison.OrdinalIgnoreCase);
    var listenAddress = inContainer ? System.Net.IPAddress.Any : System.Net.IPAddress.Loopback;
    options.Listen(listenAddress, 5127, listenOptions => listenOptions.Protocols = HttpProtocols.Http2);
});

builder.Services.AddGrpc(options =>
{
    options.MaxSendMessageSize = 100 * 1024 * 1024;
    options.MaxReceiveMessageSize = 100 * 1024 * 1024;
    options.EnableDetailedErrors = true;
});

builder.Services.AddControllers();

var app = builder.Build();

// Dev fallback: если ЮKassa не настроена — используем mock-провайдер
var yooKassaOptions = app.Services.GetRequiredService<IOptions<YooKassaOptions>>().Value;
var billingOptions = app.Services.GetRequiredService<IOptions<BillingOptions>>().Value;
if (string.IsNullOrWhiteSpace(yooKassaOptions.ShopId) || string.IsNullOrWhiteSpace(yooKassaOptions.SecretKey))
{
    if (!billingOptions.DefaultProvider.Equals("mock", StringComparison.OrdinalIgnoreCase))
    {
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("BillingStartup");
        logger.LogWarning("YooKassa credentials missing — forcing DefaultProvider=mock");
    }
}

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<BillingServiceContext>();
    db.Database.Migrate();
}

app.UseRouting();

app.MapGrpcService<BillingGrpcService>();

app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));

app.Run();

public partial class Program { }
