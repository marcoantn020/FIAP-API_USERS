# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

**Users API** is a .NET 8 production-ready authentication and user management API built with ASP.NET Core Minimal APIs, ASP.NET Identity, PostgreSQL, and JWT authentication. The project follows enterprise patterns including FluentValidation, Options Pattern, Global Exception Handling, Structured Logging (Serilog), and API Versioning.

## Essential Commands

### Build and Run
```bash
# Build the project
dotnet build

# Run the application (default: http://localhost:5242)
dotnet run

# Run without rebuilding
dotnet run --no-build

# Clean build artifacts
dotnet clean
```

### Database Migrations
```bash
# Create a new migration
dotnet ef migrations add MigrationName --project users-api.csproj --output-dir src/Data/Migrations

# Apply migrations to database
dotnet ef database update

# Remove last migration (if not applied)
dotnet ef migrations remove

# View migration SQL script
dotnet ef migrations script
```

### Configuration & Secrets
```bash
# Initialize user secrets (already done, ID: eff65771-10ba-4831-aa6f-a3324ce74b00)
dotnet user-secrets init

# Set secrets for local development
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Database=usersdb;Username=postgres;Password=yourpassword"
dotnet user-secrets set "Jwt:Secret" "your-secret-key-min-32-characters"
dotnet user-secrets set "Jwt:Issuer" "users-api"
dotnet user-secrets set "Jwt:Audience" "users-api-client"

# List all secrets
dotnet user-secrets list
```

### Docker
```bash
# Build Docker image
docker build -t users-api .

# Run PostgreSQL container for development
docker run --name postgres-dev -e POSTGRES_PASSWORD=yourpassword -e POSTGRES_DB=usersdb -p 5432:5432 -d postgres:16
```

## Architecture & Code Structure

### Minimal API with Clean Architecture Layers

The project uses **Minimal APIs** (not Controllers) with a vertical slice architecture organized by feature concerns:

```
src/
├── Common/          # Shared types (ApiResponse<T>, IAuditable)
├── Configuration/   # Options Pattern classes (JwtOptions)
├── Data/           # EF Core DbContext and Migrations
├── DTOs/           # Request/Response DTOs (records)
├── Endpoints/      # Minimal API endpoint mappings
├── Exceptions/     # Custom exception types
├── Filters/        # Endpoint filters (validation)
├── Middleware/     # Custom middleware (global error handling)
├── Models/         # Entity models (User, RefreshToken)
├── Services/       # Business logic services
└── Validators/     # FluentValidation validators
```

### Key Architectural Patterns

**1. Options Pattern for Configuration**
- All configuration uses strongly-typed `IOptions<T>` injection
- Example: `JwtOptions` is bound from `appsettings.json` section "Jwt"
- Never inject `IConfiguration` directly into services

**2. Global Exception Handling**
- `GlobalExceptionHandlerMiddleware` catches all exceptions
- Custom exceptions (`ValidationException`, `UnauthorizedException`, `BusinessException`) map to specific HTTP status codes
- Never use try/catch in endpoints - let middleware handle it

**3. Endpoint Filters for Validation**
- `ValidationFilter<T>` runs FluentValidation before endpoint execution
- Applied via `.AddEndpointFilter<ValidationFilter<RegisterRequest>>()`
- Throws `ValidationException` with error dictionary on failure

**4. Audit Pattern with EF Core Interceptor**
- `IAuditable` interface provides `CreatedAt`, `UpdatedAt`, `DeletedAt`
- `UsersDbContext.SaveChangesAsync()` automatically sets audit fields
- Applied to `User` model and any future auditable entities

**5. API Versioning**
- URL-based versioning: `/api/v1/users/register`
- Configured via `Asp.Versioning` package
- Supports URL segment, header (`X-Api-Version`), and query string (`?api-version=1.0`)

**6. Refresh Token Pattern**
- Access tokens (JWT) expire in hours (default: 24h)
- Refresh tokens stored in database, expire in days (default: 7 days)
- Automatic token rotation on refresh
- Old refresh tokens revoked (`RevokedAt` timestamp)

### ASP.NET Identity Integration

**CRITICAL**: The `User` model inherits from `IdentityUser<Guid>`, which means:
- `UserName` is **REQUIRED** by Identity (cannot be null/empty)
- When registering users, always set: `UserName = request.Email` or `UserName = request.Email.Split("@")[0]`
- `Email` must be unique (`RequireUniqueEmail = true`)
- Password validation rules are configured in `Program.cs` (min 8 chars, lockout after 5 failed attempts)

### Middleware Order (CRITICAL)

The middleware pipeline order in `Program.cs` is critical and must be maintained:

```csharp
app.UseSerilogRequestLogging();        // 1. Logging (first)
app.UseMiddleware<GlobalExceptionHandlerMiddleware>(); // 2. Error handling
app.UseSwagger();                       // 3. Swagger (dev only)
app.UseSwaggerUI();
app.UseHttpsRedirection();              // 4. HTTPS redirect
app.UseCors(...);                       // 5. CORS
app.UseRateLimiter();                   // 6. Rate limiting
app.UseAuthentication();                // 7. Authentication (before Authorization!)
app.UseAuthorization();                 // 8. Authorization
app.MapUsersEndpoints();                // 9. Endpoints
app.MapHealthChecks("/health");         // 10. Health checks
```

### Database Context

**UsersDbContext** responsibilities:
- Configures ASP.NET Identity tables for `User` with `Guid` primary keys
- Manages `RefreshTokens` table with foreign key to `Users`
- Implements audit interceptor in `SaveChangesAsync()` override
- Connection string comes from User Secrets (dev) or Environment Variables (prod)

### Swagger Configuration Notes

**IMPORTANT Package Compatibility**:
- Use `Swashbuckle.AspNetCore 6.5.0` (compatible with OpenAPI 3.0.1)
- Do **NOT** use `Microsoft.AspNetCore.OpenApi` - causes TypeLoadException
- Do **NOT** use `.WithOpenApi()` on endpoints - incompatible with current setup
- Swagger UI available at: `http://localhost:5242/swagger`

### Environment-Specific Configuration

**Development** (`appsettings.Development.json`):
- Debug-level logging
- User Secrets for sensitive data
- CORS allows all origins (`AllowAll` policy)

**Production** (`appsettings.Production.json`):
- Warning-level logging
- Environment variables for secrets
- CORS restricted to specific origins
- Rate limiting: 5 requests/minute on auth endpoints

## Common Development Workflows

### Adding a New Field to User Model

1. Add property to `src/Models/User.cs`
2. Update DTOs in `src/DTOs/Register.cs` and `src/DTOs/Login.cs`
3. Update validators in `src/Validators/`
4. Update `UserService.RegisterAsync()` to set the new field
5. **IMPORTANT**: Set `UserName` property when creating user (required by Identity)
6. Create migration: `dotnet ef migrations add AddFieldName --output-dir src/Data/Migrations`
7. Apply migration: `dotnet ef database update`
8. Build and test: `dotnet build`

### Adding a New Endpoint

1. Create DTOs in `src/DTOs/` (use `record` types)
2. Create FluentValidation validator in `src/Validators/`
3. Add service method to `src/Services/IUserService.cs` and `UserService.cs`
4. Map endpoint in `src/Endpoints/UsersEndpoints.cs`:
   ```csharp
   group.MapPost("endpoint-name", HandlerMethodAsync)
       .AddEndpointFilter<ValidationFilter<RequestDto>>()
       .WithName("OperationName")
       .WithSummary("Short description")
       .WithDescription("Detailed description")
       .Produces<ApiResponse<ResponseDto>>(StatusCodes.Status200OK)
       .WithTags("TagName");
   ```
5. Never add `.WithOpenApi()` - causes compatibility issues

### Handling Validation

FluentValidation runs automatically via `ValidationFilter<T>`:
- Define rules in `src/Validators/RequestNameValidator.cs`
- Filter throws `ValidationException` with error dictionary
- Middleware converts to 400 Bad Request with structured errors
- Example error response:
  ```json
  {
    "success": false,
    "data": null,
    "message": "Validation failed",
    "errors": {
      "Email": ["Email is required."],
      "Password": ["Password must contain at least one uppercase letter."]
    }
  }
  ```

## Code Quality Standards (Microsoft Best Practices)

### Security Checklist - EVERY New Feature/Bug Fix

**1. Input Validation (Defense in Depth)**
```csharp
// ✅ ALWAYS validate at multiple layers
// Layer 1: FluentValidation (automatic via ValidationFilter)
public class MyRequestValidator : AbstractValidator<MyRequest>
{
    public MyRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Amount).GreaterThan(0).LessThanOrEqualTo(1000000);
    }
}

// Layer 2: Business logic validation (throw BusinessException)
if (await _context.Users.AnyAsync(u => u.Email == request.Email))
    throw new BusinessException("Email already registered");

// ❌ NEVER trust client input without validation
// ❌ NEVER use string concatenation for SQL (use EF Core/LINQ)
```

**2. Secure Data Handling**
```csharp
// ✅ ALWAYS use parameterized queries (EF Core does this automatically)
var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == email);

// ✅ ALWAYS hash passwords (UserManager does this automatically)
await userManager.CreateAsync(user, request.Password);

// ✅ ALWAYS use UTC for timestamps
DateTime.UtcNow // ✅ Correct
DateTime.Now    // ❌ Wrong - timezone issues

// ❌ NEVER log sensitive data
_logger.LogInformation("User {Email} logged in", user.Email); // ✅ OK
_logger.LogInformation("Password: {Password}", password);     // ❌ NEVER

// ❌ NEVER return sensitive data in responses
return new UserResponse(user.Id, user.Email); // ✅ OK
return new UserResponse(user.PasswordHash);   // ❌ NEVER
```

**3. Authorization & Authentication**
```csharp
// ✅ ALWAYS check authorization before operations
// Add to endpoint when needed:
.RequireAuthorization()

// ✅ ALWAYS validate token ownership
var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
if (resource.UserId.ToString() != userId)
    throw new UnauthorizedException("Access denied");

// ❌ NEVER expose internal IDs or implementation details in URLs
// ✅ Use GUIDs, not sequential integers for user-facing IDs
```

**4. SQL Injection & XSS Prevention**
```csharp
// ✅ EF Core prevents SQL injection automatically - use it correctly
await _context.Users.Where(u => u.Email == email).ToListAsync();

// ❌ NEVER use FromSqlRaw with string concatenation
_context.Users.FromSqlRaw($"SELECT * FROM Users WHERE Email = '{email}'"); // ❌ VULNERABLE

// ✅ If you must use raw SQL, use parameters
_context.Users.FromSqlRaw("SELECT * FROM Users WHERE Email = {0}", email); // ✅ Safe

// API returns JSON - XSS not a concern, but still:
// ✅ Validate and sanitize all string inputs via FluentValidation
```

### Performance Checklist

**1. Database Access Patterns**
```csharp
// ✅ ALWAYS use async/await for I/O operations
await _context.Users.ToListAsync();           // ✅ Correct
var users = _context.Users.ToList();          // ❌ Blocks thread

// ✅ ALWAYS use AsNoTracking() for read-only queries
await _context.Users
    .AsNoTracking()
    .Where(u => u.CreatedAt > date)
    .ToListAsync();

// ✅ ALWAYS project only needed columns
await _context.Users
    .Select(u => new { u.Id, u.Email })       // ✅ Efficient
    .ToListAsync();

// ❌ NEVER load entire entities when you need few fields
var users = await _context.Users.ToListAsync(); // ❌ Loads everything

// ✅ ALWAYS use Include() for related data (avoid N+1 queries)
await _context.RefreshTokens
    .Include(rt => rt.User)                    // ✅ Single query
    .FirstOrDefaultAsync();

// ❌ NEVER lazy load in loops (N+1 problem)
foreach(var token in tokens) {
    var user = await _context.Users.FindAsync(token.UserId); // ❌ N queries
}

// ✅ ALWAYS add indexes for frequently queried columns
// In DbContext.OnModelCreating():
modelBuilder.Entity<RefreshToken>()
    .HasIndex(rt => rt.Token)
    .IsUnique();
```

**2. Memory & Resource Management**
```csharp
// ✅ ALWAYS use CancellationToken for long operations
public async Task<Result> LongOperationAsync(CancellationToken cancellationToken)
{
    await _context.SaveChangesAsync(cancellationToken);
}

// ✅ ALWAYS limit query results
.Take(100) // Prevent loading millions of records

// ✅ ALWAYS dispose resources (using statement - C# does automatically for most)
// DbContext is scoped - disposed automatically by DI

// ❌ NEVER return IQueryable from services to controllers/endpoints
// ✅ Always materialize with ToListAsync() inside service layer
```

**3. Caching Strategies (when needed)**
```csharp
// ✅ Use IMemoryCache for frequently accessed, rarely changed data
// Example: Configuration, lookup tables
private readonly IMemoryCache _cache;

public async Task<Settings> GetSettingsAsync()
{
    return await _cache.GetOrCreateAsync("app-settings", async entry =>
    {
        entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10);
        return await _context.Settings.FirstAsync();
    });
}

// ❌ NEVER cache user-specific sensitive data
// ❌ NEVER cache without expiration policy
```

### Code Organization & Clean Code

**1. Service Layer Patterns**
```csharp
// ✅ Service methods should be focused and single-responsibility
public async Task<LoginResponse> LoginAsync(LoginRequest request)
{
    var user = await FindUserByEmailAsync(request.Email);
    ValidatePassword(user, request.Password);
    var tokens = await GenerateTokensAsync(user);
    return MapToResponse(user, tokens);
}

// ✅ Extract complex logic to private methods
private async Task<User> FindUserByEmailAsync(string email)
{
    var user = await userManager.FindByEmailAsync(email);
    if (user is null) throw new UnauthorizedException("Invalid credentials");
    return user;
}

// ❌ NEVER put business logic in endpoints - endpoints are thin
// ❌ NEVER put data access logic in endpoints - use services
```

**2. DTOs and Mapping**
```csharp
// ✅ ALWAYS use record types for DTOs (immutable)
public record CreateUserRequest(string Email, string Password);

// ✅ ALWAYS separate Request/Response DTOs
public record CreateUserRequest(...);
public record CreateUserResponse(Guid Id, string Email, DateTime CreatedAt);

// ❌ NEVER expose entity models directly in API responses
return user; // ❌ Exposes PasswordHash, internal fields

// ✅ ALWAYS map to DTOs
return new UserResponse(user.Id, user.Email); // ✅ Controlled exposure

// ✅ Use nullable reference types correctly
public string? DisplayName { get; set; }  // ✅ Can be null
public string Email { get; set; } = string.Empty; // ✅ Never null
```

**3. Error Handling**
```csharp
// ✅ ALWAYS use custom exceptions for business logic
throw new ValidationException(errors);
throw new UnauthorizedException("Invalid credentials");
throw new BusinessException("Email already exists");

// ✅ NEVER catch and swallow exceptions
try {
    await SaveAsync();
} catch { } // ❌ Silent failure

// ✅ NEVER use exceptions for control flow
try {
    var user = users.First();
} catch {
    // ❌ Use FirstOrDefault() instead
}

// ✅ Let GlobalExceptionHandlerMiddleware handle all exceptions
// ❌ Don't add try/catch in endpoints unless you need specific handling
```

**4. Async/Await Best Practices**
```csharp
// ✅ ALWAYS await async methods
await _context.SaveChangesAsync(); // ✅ Correct

// ❌ NEVER use .Result or .Wait() - causes deadlocks
_context.SaveChangesAsync().Result; // ❌ Dangerous
_context.SaveChangesAsync().Wait(); // ❌ Dangerous

// ✅ ALWAYS return Task<T>, never Task<Task<T>>
public async Task<User> GetUserAsync() // ✅ Correct
{
    return await _context.Users.FirstAsync();
}

// ❌ NEVER use async void (except event handlers)
public async void SaveUser() { } // ❌ Can't be awaited

// ✅ Use ConfigureAwait(false) in library code (not needed in ASP.NET Core)
```

### OWASP Top 10 Prevention

**Implemented Protections:**
1. **Broken Access Control** → Use `.RequireAuthorization()` + validate ownership
2. **Cryptographic Failures** → User Secrets, Environment Variables, no secrets in code
3. **Injection** → EF Core parameterized queries, FluentValidation input validation
4. **Insecure Design** → Options Pattern, Dependency Injection, separation of concerns
5. **Security Misconfiguration** → Environment-specific configs, CORS policies, rate limiting
6. **Vulnerable Components** → Regular package updates, no deprecated packages
7. **Authentication Failures** → ASP.NET Identity, JWT with refresh tokens, rate limiting on auth
8. **Data Integrity Failures** → Audit trail (CreatedAt/UpdatedAt), refresh token revocation
9. **Logging Failures** → Serilog structured logging, no sensitive data in logs
10. **SSRF** → Not applicable (no outbound requests to user-controlled URLs)

### Code Review Checklist (Before Committing)

- [ ] **Build succeeds**: `dotnet build` with 0 errors, 0 warnings
- [ ] **Migration created** (if model changed): `dotnet ef migrations add ...`
- [ ] **Migration applied locally**: `dotnet ef database update`
- [ ] **All validations have FluentValidation rules** (no unvalidated inputs)
- [ ] **All service methods use async/await** (no blocking calls)
- [ ] **No secrets in code** (check appsettings.json is clean)
- [ ] **Custom exceptions used** (not generic Exception)
- [ ] **DTOs used for requests/responses** (not entity models)
- [ ] **Endpoint has proper metadata**: `.WithName()`, `.WithSummary()`, `.Produces<>()`
- [ ] **Database queries use AsNoTracking()** (for read-only operations)
- [ ] **Authorization added if needed**: `.RequireAuthorization()`
- [ ] **Rate limiting added if needed**: `.RequireRateLimiting("auth")`
- [ ] **No sensitive data logged** (no passwords, tokens, PII in logs)
- [ ] **UTC timestamps used** (DateTime.UtcNow, not DateTime.Now)
- [ ] **Swagger still works**: Navigate to `/swagger` and verify endpoints visible

### Performance Monitoring

**Key Metrics to Watch:**
- Response time: Auth endpoints should be < 200ms, other endpoints < 100ms
- Database query count: Check EF Core logs for N+1 queries
- Memory usage: Monitor for memory leaks (DbContext disposal)
- Thread pool starvation: Use async/await correctly

**Enable EF Core Query Logging (Debug only):**
```json
// appsettings.Development.json
{
  "Logging": {
    "LogLevel": {
      "Microsoft.EntityFrameworkCore.Database.Command": "Information"
    }
  }
}
```

## Rate Limiting

Two policies configured:
- **`"fixed"`**: 100 requests/minute (general)
- **`"auth"`**: 5 requests/minute per IP on `/register`, `/login`, `/refresh-token`

Apply to endpoint: `.RequireRateLimiting("auth")`

## Health Checks

Endpoint: `GET /health`
- Returns 200 Healthy if database is reachable
- Returns 503 Unhealthy if database connection fails
- Checks registered: `database` (EF Core), `self` (always healthy)

## Logging with Serilog

Structured logging configured with:
- Console sink (always enabled)
- File sink: `logs/users-api-YYYY-MM-DD.log` (30-day retention, daily rotation)
- Enrichers: `Application`, `Environment`, `LogContext`

Access logger via constructor injection: `ILogger<T>` (avoid - middleware handles most logging needs)

## JWT Configuration

Claims included in JWT:
- `sub`: User ID (Guid)
- `email`: User email
- `jti`: Unique token ID

Token validation parameters configured in `Program.cs`:
- ValidateIssuer, ValidateAudience, ValidateLifetime, ValidateIssuerSigningKey all `true`
- Signing key from `Jwt:Secret` configuration (min 32 characters recommended)

## Known Issues & Constraints

1. **Swagger Package Version**: Must use `Swashbuckle.AspNetCore 6.5.0` - newer versions incompatible
2. **UserName Required**: ASP.NET Identity requires `UserName` - always set it when creating users
3. **Migration Output Directory**: Always use `--output-dir src/Data/Migrations` to maintain structure
4. **No .WithOpenApi()**: Causes TypeLoadException - removed from all endpoints

## Production Deployment Checklist

- [ ] Set User Secrets or Environment Variables for all sensitive configuration
- [ ] Update CORS `AllowedOrigins` in `appsettings.Production.json`
- [ ] Configure PostgreSQL connection string with strong password
- [ ] Set `ASPNETCORE_ENVIRONMENT=Production`
- [ ] Review rate limiting thresholds
- [ ] Enable HTTPS with valid certificate
- [ ] Configure log retention policy
- [ ] Set up database backups
- [ ] Review JWT secret strength (min 32 chars, cryptographically random)
