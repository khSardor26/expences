using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using MyExpenses.Api.Data;
using MyExpenses.Api.Endpoints;
using MyExpenses.Api.Security;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

builder.Services.Configure<JsonOptions>(options =>
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull);

// Bad JSON (such as a decimal amount) gets a 400 response in every environment.
builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = false);
builder.Services.AddProblemDetails();

builder.Services.AddIdentityCore<AppUser>(options =>
    {
        options.User.RequireUniqueEmail = true;
        options.Password.RequiredLength = 10;
        options.Password.RequireDigit = false;
        options.Password.RequireLowercase = false;
        options.Password.RequireUppercase = false;
        options.Password.RequireNonAlphanumeric = false;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    })
    .AddEntityFrameworkStores<AppDbContext>()
    .AddSignInManager();

builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme).AddIdentityCookies();

var isDevelopment = builder.Environment.IsDevelopment();
builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = "myexpenses.session";
    options.Cookie.HttpOnly = true; // scripts can't read it
    options.Cookie.SameSite = SameSiteMode.Strict; // other sites can't send requests with it
    options.Cookie.SecurePolicy = isDevelopment ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    options.ExpireTimeSpan = TimeSpan.FromDays(30);
    options.SlidingExpiration = true;

    // This is an API: answer with a status code rather than redirecting to a login page.
    options.Events.OnRedirectToLogin = context =>
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    };
    options.Events.OnRedirectToAccessDenied = context =>
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    };
});

// Every endpoint needs a signed-in user unless it explicitly opts out,
// so a new endpoint can't be left open by accident.
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

// Checked on every request, so "sign out everywhere" takes effect at once.
builder.Services.Configure<SecurityStampValidatorOptions>(options => options.ValidationInterval = TimeSpan.Zero);

// The bot token is part of Telegram's URLs, so these requests must never be logged.
builder.Services.AddHttpClient<ITelegramClient, TelegramClient>().RemoveAllLoggers();
builder.Services.AddScoped<SecurityChallengeService>();
builder.Services.AddScoped<TelegramLinkService>();
builder.Services.AddHostedService<TelegramUpdatePoller>();

var authPermitsPerMinute =builder.Configuration.GetValue("Auth:RateLimitPerMinute", 20);
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(AuthEndpoints.RateLimitPolicy, context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = authPermitsPerMinute,
                Window = TimeSpan.FromMinutes(1),
            }));
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
}

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapAuthEndpoints();
app.MapSecurityEndpoints();
app.MapDataEndpoints();
app.MapIncomeEndpoints();
app.MapExpenseEndpoints();
app.MapCategoryEndpoints();
app.MapItemEndpoints();

await app.RunAsync();

public partial class Program;
