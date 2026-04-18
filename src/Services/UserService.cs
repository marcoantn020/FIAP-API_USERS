using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
using users_api.DTOs;
using users_api.Models;
using JwtRegisteredClaimNames = Microsoft.IdentityModel.JsonWebTokens.JwtRegisteredClaimNames;

namespace users_api.Services;

public class UserService(
    UserManager<User> userManager,
    IConfiguration configuration
    ) : IUserService
{
    private readonly IConfiguration _configKey = configuration.GetSection("Jwt");
    
    public async Task<RegisterResponse> RegisterAsync(RegisterRequest request)
    {
        var user = new User
        {
            UserName = request.Username,
            Email = request.Email
        };
        
        var result = await userManager.CreateAsync(user, request.Password);
        if (result.Succeeded) return new RegisterResponse(user.Id.ToString(), user.UserName, user.Email);
        var errors = string.Join(", ", result.Errors.Select(e => e.Description));
        throw new InvalidOperationException(errors);

    }

    public async Task<LoginResponse> LoginAsync(LoginRequest request)
    {
        var user = await userManager.FindByEmailAsync(request.Email);
        if (user is null) throw new UnauthorizedAccessException("Credenciais invalidas.");

        var valid = await userManager.CheckPasswordAsync(user, request.Password);
        if (!valid) throw new UnauthorizedAccessException("Credenciais invalidas.");

        var token = GenerateJwt(user);
        var expires = DateTime.UtcNow.AddHours(_configKey.GetValue<int>("ExpirationHours"));
        return new LoginResponse(
            token,
            expires,
            user.UserName!,
            user.Id.ToString()
            );
    }

    private string GenerateJwt(User user)
    {
        
        if (_configKey["Secret"] is null ) 
            throw new InvalidOperationException("Chave JWT não configurada e/ou invalida");

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_configKey["Secret"]!));

        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email!),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };
        
        var token = new JwtSecurityToken(
            issuer: _configKey["Issuer"],
            audience: _configKey["Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddHours(_configKey.GetValue<int>("ExpirationHours")),
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}