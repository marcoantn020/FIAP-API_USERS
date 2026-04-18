namespace users_api.DTOs;

public record LoginRequest(
    string Email,
    string Password
    );
    
public record LoginResponse(
    string AccessToken,
    string RefreshToken,
    DateTime AccessTokenExpireAt,
    DateTime RefreshTokenExpireAt,
    string Username,
    string UserId
    );