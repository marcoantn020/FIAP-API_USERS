namespace users_api.DTOs;

public record LoginRequest(
    string Email,
    string Password
    );
    
public record LoginResponse(
    string Token,
    DateTime ExpireAt,
    string Username,
    string UserId
    );