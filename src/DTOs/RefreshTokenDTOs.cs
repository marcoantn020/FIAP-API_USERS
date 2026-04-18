namespace users_api.DTOs;

public record RefreshTokenRequest(
    string RefreshToken
);

public record RefreshTokenResponse(
    string AccessToken,
    string RefreshToken,
    DateTime AccessTokenExpireAt,
    DateTime RefreshTokenExpireAt
);
