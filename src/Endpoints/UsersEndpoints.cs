using Asp.Versioning;
using Asp.Versioning.Builder;
using users_api.Common;
using users_api.Filters;
using users_api.Services;
using users_api.DTOs;

namespace users_api.Endpoints;

public static class UsersEndpoints
{
    public static void MapUsersEndpoints(this WebApplication app)
    {
        var versionSet = app.NewApiVersionSet()
            .HasApiVersion(new ApiVersion(1, 0))
            .ReportApiVersions()
            .Build();

        var group = app.MapGroup("api/v{version:apiVersion}/users")
            .WithApiVersionSet(versionSet);

        group.MapPost("register", RegisterAsync)
            .AddEndpointFilter<ValidationFilter<RegisterRequest>>()
            .RequireRateLimiting("auth")
            .WithName("RegisterUser")
            .WithSummary("Register a new user")
            .WithDescription("Creates a new user account with email and password")
            .Produces<ApiResponse<RegisterResponse>>(StatusCodes.Status201Created)
            .Produces<ApiResponse<object>>(StatusCodes.Status400BadRequest)
            .WithTags("Authentication");

        group.MapPost("login", LoginAsync)
            .AddEndpointFilter<ValidationFilter<LoginRequest>>()
            .RequireRateLimiting("auth")
            .WithName("LoginUser")
            .WithSummary("Authenticate user")
            .WithDescription("Authenticates a user and returns a JWT token")
            .Produces<ApiResponse<LoginResponse>>(StatusCodes.Status200OK)
            .Produces<ApiResponse<object>>(StatusCodes.Status401Unauthorized)
            .WithTags("Authentication");

        group.MapPost("refresh-token", RefreshTokenAsync)
            .AddEndpointFilter<ValidationFilter<RefreshTokenRequest>>()
            .RequireRateLimiting("auth")
            .WithName("RefreshToken")
            .WithSummary("Refresh access token")
            .WithDescription("Generates a new access token using a valid refresh token")
            .Produces<ApiResponse<RefreshTokenResponse>>(StatusCodes.Status200OK)
            .Produces<ApiResponse<object>>(StatusCodes.Status401Unauthorized)
            .WithTags("Authentication");
    }

    private static async Task<IResult> RegisterAsync(
        RegisterRequest request,
        IUserService userService)
    {
        var response = await userService.RegisterAsync(request);
        return Results.Created(
            $"/users/{response.UserId}",
            ApiResponse<RegisterResponse>.SuccessResponse(response, "User registered successfully.")
        );
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest request,
        IUserService userService)
    {
        var response = await userService.LoginAsync(request);
        return Results.Ok(
            ApiResponse<LoginResponse>.SuccessResponse(response, "Login successful.")
        );
    }

    private static async Task<IResult> RefreshTokenAsync(
        RefreshTokenRequest request,
        IUserService userService)
    {
        var response = await userService.RefreshTokenAsync(request);
        return Results.Ok(
            ApiResponse<RefreshTokenResponse>.SuccessResponse(response, "Token refreshed successfully.")
        );
    }
}