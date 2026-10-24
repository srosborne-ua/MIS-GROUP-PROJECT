using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using API.Concierge;
using API.Database;
using API.Services;

// Secrets (ANTHROPIC_API_KEY) come from the repo-root .env; load them before anything reads the environment.
DotEnv.Load();

// The API also serves the site. Locally it serves the repo's Client/ folder directly, so
// edits show on refresh; a published build carries a copy of Client/ in wwwroot instead.
var localClient = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "Client"));
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    WebRootPath = Directory.Exists(localClient) ? localClient : null
});

// Add services to the container.

// HotelStore does its own validation so the messages read like the rest of the site.
builder.Services.AddControllers(options =>
    {
        options.Filters.Add<HotelRuleExceptionFilter>();
        options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;
    })
    .AddJsonOptions(options =>
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull);
// MySQL as druid_app; the connection string is DB_APP_CONNECTION from .env.
builder.Services.AddSingleton<Db>();
builder.Services.AddSingleton<HotelStore>();

// Druid AI Assistant (POST api/concierge). Register concierge tools here as
// IConciergeTool singletons once they are approved.
// Availability and prices through HotelStore, the same search the booking page uses.
builder.Services.AddSingleton<IConciergeTool, CheckAvailabilityTool>();
// Database lookups (RAG): read-only SELECTs as druid_llm_ro, only when DB_LLM_RO_CONNECTION is set.
builder.Services.AddSingleton<ReadOnlyDb>();
if (ReadOnlyDb.IsConfigured)
{
    builder.Services.AddSingleton<IConciergeTool, GetKnowledgeSchemaTool>();
    builder.Services.AddSingleton<IConciergeTool, SearchKnowledgeTool>();
}
builder.Services.AddSingleton<ConciergePrompt>();
builder.Services.AddSingleton<ConciergeTools>();
builder.Services.AddSingleton<ConciergeService>();

// Cap AI requests per visitor to protect API spend: 20 messages a minute per IP.
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("concierge", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1) }));
    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        await context.HttpContext.Response.WriteAsJsonAsync(
            new { message = "Too many messages. Please wait a minute and try again." }, cancellationToken);
    };
});
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

// Serve index.html at / plus its styles, scripts, and images.
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseRateLimiter();

app.UseAuthorization();

app.MapControllers();

app.Run();
