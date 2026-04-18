using users_api.Services;
using users_api.DTOs;

namespace users_api.Endpoints;

public static class UsersEndpoints
{
    public static void MapUsersEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/users")
            .WithOpenApi();

        group.MapPost("register", RegisterAsync);
        group.MapPost("login", LoginAsync);
    }

    private static async Task<IResult> RegisterAsync(
        RegisterRequest request,
        IUserService userService)
    {
        try
        {
            var response = await userService.RegisterAsync(request);
            return Results.Created($"/users/{response.UserId}", response);
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            return Results.Conflict(new { message = e.Message });
        }
    }
    
    private static async Task<IResult> LoginAsync(
        LoginRequest request,
        IUserService userService)
    {
        try
        {
            var response = await userService.LoginAsync(request);
            return Results.Ok(response);
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            return Results.Unauthorized();
        }
    }
}