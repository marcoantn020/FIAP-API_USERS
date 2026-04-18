namespace users_api.DTOs;

public record RegisterRequest(
    string DisplayName,
    string Password,
    string Email,
    string? Role = "User"
    );
    
public record RegisterResponse(
    string? UserId,
    string DisplayName,
    string Email
    );