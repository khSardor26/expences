using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using MyExpenses.Api.Data;
using MyExpenses.Api.Security;

namespace MyExpenses.Api.Endpoints;

public static class AuthEndpoints
{
    public const string RateLimitPolicy = "auth";

    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/api/auth");

        auth.MapPost("/register", Register).AllowAnonymous().RequireRateLimiting(RateLimitPolicy);
        auth.MapPost("/login", Login).AllowAnonymous().RequireRateLimiting(RateLimitPolicy);
        auth.MapPost("/login/verify", VerifyLogin).AllowAnonymous().RequireRateLimiting(RateLimitPolicy);
        auth.MapPost("/login/resend", ResendLoginCode).AllowAnonymous().RequireRateLimiting(RateLimitPolicy);
        auth.MapPost("/logout", Logout);
        auth.MapGet("/me", (ClaimsPrincipal user) =>
            Results.Ok(new UserDto(user.FindFirstValue(ClaimTypes.Email) ?? user.Identity?.Name ?? "")));
    }

    private static IResult TooManyAttempts() => Results.Problem(
        "Too many failed attempts. Try again in 15 minutes.",
        statusCode: StatusCodes.Status429TooManyRequests);

    private static IResult ExpiredLogin() => Results.Problem(
        "This sign-in has expired. Enter your password again.",
        statusCode: StatusCodes.Status401Unauthorized);

    private static async Task<IResult> Register(
        CredentialsRequest request,
        AppDbContext db,
        UserManager<AppUser> users,
        SignInManager<AppUser> signIn)
    {
        var validator = new Validator();
        validator.Required("email", request.Email, 256);
        validator.Required("password", request.Password, 256);
        if (!validator.IsValid) return validator.Problem();

        var user = new AppUser { UserName = request.Email, Email = request.Email };

        // One transaction, so an account can never exist without its default categories.
        await using var transaction = await db.Database.BeginTransactionAsync();

        var created = await users.CreateAsync(user, request.Password!);
        if (!created.Succeeded)
        {
            foreach (var error in created.Errors) validator.Add(error.Code, error.Description);
            return validator.Problem();
        }

        SeedData.AddTo(db, user.Id);
        await db.SaveChangesAsync();
        await transaction.CommitAsync();

        await signIn.SignInAsync(user, isPersistent: true);
        return Results.Ok(new UserDto(user.Email!));
    }

    /// <summary>Checks the password. With Telegram on, this sends a code rather than signing in.</summary>
    private static async Task<IResult> Login(
        CredentialsRequest request,
        UserManager<AppUser> users,
        SignInManager<AppUser> signIn,
        SecurityChallengeService challenges)
    {
        var user = string.IsNullOrWhiteSpace(request.Email) ? null : await users.FindByEmailAsync(request.Email);
        if (user is null) return WrongCredentials();

        var result = await signIn.CheckPasswordSignInAsync(user, request.Password ?? "", lockoutOnFailure: true);
        if (result.IsLockedOut) return TooManyAttempts();
        if (!result.Succeeded) return WrongCredentials();

        if (!user.TelegramEnabled)
        {
            await signIn.SignInAsync(user, isPersistent: true);
            return Results.Ok(new LoginResponse(user.Email, RequiresCode: false, ChallengeId: null, Delivered: null));
        }

        var (challenge, delivered) = await challenges.StartAsync(user, ChallengePurpose.SignIn);
        return Results.Ok(new LoginResponse(null, RequiresCode: true, challenge.Id, delivered));
    }

    private static IResult WrongCredentials() => Results.Problem(
        "Incorrect email or password.", statusCode: StatusCodes.Status401Unauthorized);

    /// <summary>The second step of signing in: only a correct code or recovery code starts a session.</summary>
    private static async Task<IResult> VerifyLogin(
        VerifyLoginRequest request,
        AppDbContext db,
        UserManager<AppUser> users,
        SignInManager<AppUser> signIn,
        SecurityChallengeService challenges)
    {
        var challenge = string.IsNullOrWhiteSpace(request.ChallengeId) ? null : await db.SecurityChallenges.FindAsync(request.ChallengeId);
        if (challenge is null || challenge.Purpose != ChallengePurpose.SignIn) return ExpiredLogin();

        var user = await users.FindByIdAsync(challenge.UserId);
        if (user is null) return ExpiredLogin();
        if (await users.IsLockedOutAsync(user)) return TooManyAttempts();

        switch (await challenges.CheckAsync(challenge, user, request.Code, request.RecoveryCode))
        {
            case CheckResult.Valid:
                await signIn.SignInAsync(user, isPersistent: true);
                return Results.Ok(new UserDto(user.Email!));

            case CheckResult.Wrong:
                return Results.Problem(
                    "That code isn't right. Check it and try again.",
                    statusCode: StatusCodes.Status400BadRequest);

            case CheckResult.TooManyAttempts:
                // Stops someone who has the password from guessing codes by signing in again and again.
                await users.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow + SecurityChallengeService.LockoutAfterTooManyCodes);
                return TooManyAttempts();

            default:
                return ExpiredLogin();
        }
    }

    private static async Task<IResult> ResendLoginCode(
        ChallengeRequest request,
        AppDbContext db,
        UserManager<AppUser> users,
        SecurityChallengeService challenges)
    {
        var challenge = string.IsNullOrWhiteSpace(request.ChallengeId) ? null : await db.SecurityChallenges.FindAsync(request.ChallengeId);
        if (challenge is null || challenge.Purpose != ChallengePurpose.SignIn) return ExpiredLogin();

        var user = await users.FindByIdAsync(challenge.UserId);
        if (user is null) return ExpiredLogin();
        if (await users.IsLockedOutAsync(user)) return TooManyAttempts();

        var wait = SecurityChallengeService.SecondsUntilResend(challenge);
        if (wait > 0) return ResendTooSoon(wait);

        return Results.Ok(new ResendResponse(await challenges.ResendAsync(challenge, user)));
    }

    public static IResult ResendTooSoon(int seconds) => Results.Problem(
        $"Please wait {seconds} seconds before asking for another code.",
        statusCode: StatusCodes.Status429TooManyRequests);

    private static async Task<IResult> Logout(SignInManager<AppUser> signIn)
    {
        await signIn.SignOutAsync();
        return Results.NoContent();
    }
}
