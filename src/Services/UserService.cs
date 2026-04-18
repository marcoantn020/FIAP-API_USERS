using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using users_api.Configuration;
using users_api.Data;
using users_api.DTOs;
using users_api.Exceptions;
using users_api.Models;
using JwtRegisteredClaimNames = Microsoft.IdentityModel.JsonWebTokens.JwtRegisteredClaimNames;

namespace users_api.Services;

public class UserService(
    UserManager<User> userManager,
    UsersDbContext context,
    IOptions<JwtOptions> jwtOptions
    ) : IUserService
{
    private readonly JwtOptions _jwtOptions = jwtOptions.Value;
    
    public async Task<RegisterResponse> RegisterAsync(RegisterRequest request)
    {
        var user = new User
        {
            UserName = request.Email.Split("@")[0],
            DisplayName = request.DisplayName,
            Email = request.Email
        };

        var result = await userManager.CreateAsync(user, request.Password);
        if (result.Succeeded) return new RegisterResponse(user.Id.ToString(), user.DisplayName!, user.Email);

        var errors = result.Errors.ToDictionary(
            e => e.Code,
            e => new[] { e.Description }
        );
        throw new ValidationException(errors);

    }

    public async Task<LoginResponse> LoginAsync(LoginRequest request)
    {
        var user = await userManager.FindByEmailAsync(request.Email);
        if (user is null) throw new UnauthorizedException("Invalid credentials.");

        var valid = await userManager.CheckPasswordAsync(user, request.Password);
        if (!valid) throw new UnauthorizedException("Invalid credentials.");

        var accessToken = GenerateJwt(user);
        var accessTokenExpires = DateTime.UtcNow.AddHours(_jwtOptions.ExpirationHours);

        var refreshToken = await GenerateRefreshTokenAsync(user.Id);

        return new LoginResponse(
            accessToken,
            refreshToken.Token,
            accessTokenExpires,
            refreshToken.ExpiresAt,
            user.DisplayName!,
            user.Id.ToString()
        );
    }

    public async Task<RefreshTokenResponse> RefreshTokenAsync(RefreshTokenRequest request)
    {
        var storedToken = await context.RefreshTokens
            .Include(rt => rt.User)
            .FirstOrDefaultAsync(rt => rt.Token == request.RefreshToken);

        if (storedToken is null || !storedToken.IsActive)
            throw new UnauthorizedException("Invalid or expired refresh token.");

        // Revoke old refresh token
        storedToken.RevokedAt = DateTime.UtcNow;

        // Generate new tokens
        var accessToken = GenerateJwt(storedToken.User);
        var accessTokenExpires = DateTime.UtcNow.AddHours(_jwtOptions.ExpirationHours);
        var newRefreshToken = await GenerateRefreshTokenAsync(storedToken.UserId);

        await context.SaveChangesAsync();

        return new RefreshTokenResponse(
            accessToken,
            newRefreshToken.Token,
            accessTokenExpires,
            newRefreshToken.ExpiresAt
        );
    }

    private string GenerateJwt(User user)
    {
        if (string.IsNullOrEmpty(_jwtOptions.Secret))
            throw new InvalidOperationException("Chave JWT não configurada e/ou invalida");

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtOptions.Secret));

        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email!),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var token = new JwtSecurityToken(
            issuer: _jwtOptions.Issuer,
            audience: _jwtOptions.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(_jwtOptions.ExpirationHours),
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private async Task<RefreshToken> GenerateRefreshTokenAsync(Guid userId)
    {
        var refreshToken = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64)),
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(7) // 7 days validity
        };

        context.RefreshTokens.Add(refreshToken);
        await context.SaveChangesAsync();

        return refreshToken;
    }
}