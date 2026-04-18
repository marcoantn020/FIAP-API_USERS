namespace users_api.DTOs;

public record RegisterRequest(
    string DisplayName,
    string Password,
    string Email
    );
    
public record RegisterResponse(
    string? UserId,
    string DisplayName,
    string Email
    );