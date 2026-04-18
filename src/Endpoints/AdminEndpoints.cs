using System.Security.Claims;
using Asp.Versioning;
using Asp.Versioning.Builder;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using users_api.Common;
using users_api.Data;
using users_api.Models;

namespace users_api.Endpoints;

public static class AdminEndpoints
{
    public static void MapAdminEndpoints(this WebApplication app)
    {
        var versionSet = app.NewApiVersionSet()
            .HasApiVersion(new ApiVersion(1, 0))
            .ReportApiVersions()
            .Build();

        var group = app.MapGroup("api/v{version:apiVersion}/admin")
            .WithApiVersionSet(versionSet)
            .RequireAuthorization("AdminOnly");

        group.MapGet("users", GetAllUsersAsync)
            .WithName("GetAllUsers")
            .WithSummary("Get all users (Admin only)")
            .WithDescription("Returns a list of all registered users. Requires Admin role.")
            .Produces<ApiResponse<List<UserDto>>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .WithTags("Admin");

        group.MapGet("users/{id:guid}", GetUserByIdAsync)
            .WithName("GetUserById")
            .WithSummary("Get user by ID (Admin only)")
            .WithDescription("Returns detailed information about a specific user. Requires Admin role.")
            .Produces<ApiResponse<UserDto>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .WithTags("Admin");

        group.MapDelete("users/{id:guid}", DeleteUserAsync)
            .WithName("DeleteUser")
            .WithSummary("Delete user (Admin only)")
            .WithDescription("Soft deletes a user by setting DeletedAt timestamp. Requires Admin role.")
            .Produces<ApiResponse<object>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .WithTags("Admin");
    }

    private static async Task<IResult> GetAllUsersAsync(
        UsersDbContext context,
        UserManager<User> userManager)
    {
        var users = await context.Users
            .AsNoTracking()
            .Where(u => u.DeletedAt == null)
            .ToListAsync();

        var userDtos = new List<UserDto>();
        foreach (var user in users)
        {
            var roles = await userManager.GetRolesAsync(user);
            userDtos.Add(new UserDto(
                user.Id,
                user.Email!,
                user.DisplayName!,
                roles.ToList(),
                user.CreatedAt,
                user.UpdatedAt
            ));
        }

        return Results.Ok(
            ApiResponse<List<UserDto>>.SuccessResponse(userDtos, "Users retrieved successfully.")
        );
    }

    private static async Task<IResult> GetUserByIdAsync(
        Guid id,
        UsersDbContext context,
        UserManager<User> userManager)
    {
        var user = await context.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == id && u.DeletedAt == null);

        if (user is null)
            return Results.NotFound(
                ApiResponse<object>.ErrorResponse("User not found.")
            );

        var roles = await userManager.GetRolesAsync(user);
        var userDto = new UserDto(
            user.Id,
            user.Email!,
            user.DisplayName!,
            roles.ToList(),
            user.CreatedAt,
            user.UpdatedAt
        );

        return Results.Ok(
            ApiResponse<UserDto>.SuccessResponse(userDto, "User retrieved successfully.")
        );
    }

    private static async Task<IResult> DeleteUserAsync(
        Guid id,
        UsersDbContext context,
        ClaimsPrincipal claimsPrincipal)
    {
        var user = await context.Users.FindAsync(id);

        if (user is null || user.DeletedAt != null)
            return Results.NotFound(
                ApiResponse<object>.ErrorResponse("User not found.")
            );

        // Prevent admin from deleting themselves
        var currentUserId = claimsPrincipal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (currentUserId == id.ToString())
            return Results.BadRequest(
                ApiResponse<object>.ErrorResponse("You cannot delete your own account.")
            );

        // Soft delete
        user.DeletedAt = DateTime.UtcNow;
        await context.SaveChangesAsync();

        return Results.Ok(
            ApiResponse<object?>.SuccessResponse(null, "User deleted successfully.")
        );
    }
}

public record UserDto(
    Guid Id,
    string Email,
    string DisplayName,
    List<string> Roles,
    DateTime CreatedAt,
    DateTime? UpdatedAt
);
