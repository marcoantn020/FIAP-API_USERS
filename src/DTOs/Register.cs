namespace users_api.DTOs;

public record RegisterRequest(
    string Username,
    string Password,
    string Email
    );
    
public record RegisterResponse(
    string? UserId,
    string Username,
    string Email
    );