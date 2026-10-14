using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using API.Concierge;
using API.Services;

// Secrets (ANTHROPIC_API_KEY) come from the repo-root .env; load them before anything reads the environment.
DotEnv.Load();

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

// HotelStore does its own validation so the messages read like the rest of the site.
builder.Services.AddControllers(options =>
    {
        options.Filters.Add<HotelRuleExceptionFilter>();
        options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;
    })
    .AddJsonOptions(options =>
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull);
builder.Services.AddSingleton<HotelStore>();

// Druid AI Assistant (POST api/concierge). Register concierge tools here as
// IConciergeTool singletons once they are approved.
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

// Allow the static client (served separately) to call this API.
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod());
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseCors();

app.UseRateLimiter();

app.UseAuthorization();

app.MapControllers();

app.Run();
