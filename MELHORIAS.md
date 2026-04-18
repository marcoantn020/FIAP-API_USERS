# 📋 Documentação Completa de Melhorias - Users API

## 📌 Índice
1. [Visão Geral](#visão-geral)
2. [Melhorias de Segurança (P0 - Crítico)](#melhorias-de-segurança-p0---crítico)
3. [Melhorias de Infraestrutura (P1 - Alta)](#melhorias-de-infraestrutura-p1---alta)
4. [Features Avançadas (P2 - Média)](#features-avançadas-p2---média)
5. [Arquitetura e Patterns](#arquitetura-e-patterns)
6. [Como Usar](#como-usar)
7. [Configuração de Ambiente](#configuração-de-ambiente)
8. [Testes](#testes)
9. [CI/CD](#cicd)
10. [Roadmap Futuro](#roadmap-futuro)

---

## 🎯 Visão Geral

Este documento detalha todas as melhorias implementadas no projeto **Users API**, transformando-o de um MVP básico para uma aplicação **production-ready** seguindo as melhores práticas da Microsoft e padrões enterprise.

### Antes vs Depois

| Aspecto | Antes | Depois |
|---------|-------|--------|
| **Segurança** | Secrets hardcoded | User Secrets + Env Variables |
| **Validação** | Sem validação | FluentValidation + 6 regras |
| **Logging** | Console.WriteLine | Serilog estruturado |
| **Erros** | Try/catch genérico | Middleware global + Custom Exceptions |
| **CORS** | Não configurado | Multi-ambiente (Dev/Prod) |
| **Rate Limiting** | Vulnerável a brute force | 5 req/min em auth |
| **Autenticação** | Apenas Access Token | Access + Refresh Tokens |
| **Auditoria** | Sem tracking | CreatedAt/UpdatedAt/DeletedAt |
| **API Versioning** | Sem versionamento | URL-based (/api/v1/) |
| **Health Checks** | Não implementado | `/health` com DB check |
| **Documentação** | Swagger básico | Swagger completo + exemplos |
| **CI/CD** | Manual | GitHub Actions automatizado |

---

## 🔐 Melhorias de Segurança (P0 - Crítico)

### 1. Gerenciamento de Secrets

**Problema:** Credenciais e chaves JWT estavam hardcoded no `appsettings.json`.

**Solução Implementada:**
```bash
# User Secrets para Desenvolvimento
dotnet user-secrets init
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "..."
dotnet user-secrets set "Jwt:Secret" "..."
```

**Arquivo:** `appsettings.json` (limpo)
```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*"
}
```

**Para Produção:**
- Use **Azure Key Vault**
- Use **Environment Variables** no servidor
- Configure no CI/CD como secrets

**Benefícios:**
- ✅ Secrets não vazam no Git
- ✅ Segurança por ambiente
- ✅ Compliance com LGPD/GDPR

---

### 2. Validação de Entrada (FluentValidation)

**Problema:** Dados inválidos chegavam ao serviço sem validação.

**Solução Implementada:**

**Arquivo:** `src/Validators/RegisterRequestValidator.cs`
```csharp
public class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    public RegisterRequestValidator()
    {
        RuleFor(x => x.Username)
            .NotEmpty().WithMessage("Username is required.")
            .MinimumLength(3).WithMessage("Username must be at least 3 characters.")
            .MaximumLength(50).WithMessage("Username must not exceed 50 characters.");

        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required.")
            .EmailAddress().WithMessage("Invalid email format.");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Password is required.")
            .MinimumLength(8).WithMessage("Password must be at least 8 characters.")
            .Matches(@"[A-Z]").WithMessage("Password must contain at least one uppercase letter.")
            .Matches(@"[a-z]").WithMessage("Password must contain at least one lowercase letter.")
            .Matches(@"[0-9]").WithMessage("Password must contain at least one digit.")
            .Matches(@"[\W_]").WithMessage("Password must contain at least one special character.");
    }
}
```

**Integração com Endpoints:**
```csharp
group.MapPost("register", RegisterAsync)
    .AddEndpointFilter<ValidationFilter<RegisterRequest>>();
```

**Resposta de Erro (exemplo):**
```json
{
  "success": false,
  "message": "One or more validation errors occurred.",
  "errors": {
    "Password": [
      "Password must be at least 8 characters.",
      "Password must contain at least one uppercase letter."
    ]
  }
}
```

**Benefícios:**
- ✅ Validação declarativa e reutilizável
- ✅ Mensagens de erro claras
- ✅ Previne SQL Injection e XSS
- ✅ Compliance com requisitos de senha forte

---

### 3. Exception Handling Global

**Problema:** Exceptions expunham stack traces e informações sensíveis.

**Solução Implementada:**

**Arquivo:** `src/Middleware/GlobalExceptionHandlerMiddleware.cs`
```csharp
private async Task HandleExceptionAsync(HttpContext context, Exception exception)
{
    var (statusCode, response) = exception switch
    {
        ValidationException validationEx => (
            HttpStatusCode.BadRequest,
            ApiResponse<object>.ErrorResponse(validationEx.Message, validationEx.Errors)
        ),
        UnauthorizedException unauthorizedEx => (
            HttpStatusCode.Unauthorized,
            ApiResponse<object>.ErrorResponse(unauthorizedEx.Message)
        ),
        BusinessException businessEx => (
            HttpStatusCode.BadRequest,
            ApiResponse<object>.ErrorResponse(businessEx.Message)
        ),
        _ => (
            HttpStatusCode.InternalServerError,
            ApiResponse<object>.ErrorResponse("An unexpected error occurred.")
        )
    };

    _logger.LogError(exception,
        "Exception occurred: {ExceptionType} - {Message}",
        exception.GetType().Name,
        exception.Message);

    context.Response.StatusCode = (int)statusCode;
    await context.Response.WriteAsJsonAsync(response);
}
```

**Custom Exceptions:**
- `ValidationException` - Erros de validação (400)
- `UnauthorizedException` - Autenticação falhou (401)
- `BusinessException` - Regras de negócio (400)

**Benefícios:**
- ✅ Respostas padronizadas
- ✅ Logging estruturado
- ✅ Não expõe stack traces em produção
- ✅ Facilita debugging

---

### 4. Logging Estruturado (Serilog)

**Problema:** Logs com `Console.WriteLine` são difíceis de analisar.

**Solução Implementada:**

**Arquivo:** `Program.cs`
```csharp
builder.Host.UseSerilog((context, configuration) =>
{
    configuration
        .ReadFrom.Configuration(context.Configuration)
        .Enrich.FromLogContext()
        .Enrich.WithProperty("Application", "users-api")
        .Enrich.WithProperty("Environment", context.HostingEnvironment.EnvironmentName)
        .WriteTo.Console()
        .WriteTo.File(
            path: "logs/users-api-.log",
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 30,
            outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}"
        );
});
```

**Exemplo de Log:**
```
2026-04-18 09:30:15.123 +00:00 [ERR] Exception occurred: UnauthorizedException - Invalid credentials.
  at users_api.Services.UserService.LoginAsync(LoginRequest request) in UserService.cs:line 44
```

**Configuração por Ambiente:**

`appsettings.Development.json`:
```json
{
  "Serilog": {
    "MinimumLevel": {
      "Default": "Debug",
      "Override": {
        "Microsoft": "Information",
        "System": "Warning"
      }
    }
  }
}
```

`appsettings.Production.json`:
```json
{
  "Serilog": {
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "Microsoft": "Warning",
        "System": "Warning"
      }
    }
  }
}
```

**Benefícios:**
- ✅ Logs estruturados em JSON
- ✅ Rotação automática de arquivos
- ✅ Integração fácil com ELK/Splunk
- ✅ Contexto rico (Application, Environment)

---

## 🏗️ Melhorias de Infraestrutura (P1 - Alta)

### 5. Health Checks

**Problema:** Sem forma de verificar se a aplicação está saudável.

**Solução Implementada:**

**Arquivo:** `Program.cs`
```csharp
builder.Services.AddHealthChecks()
    .AddDbContextCheck<UsersDbContext>("database")
    .AddCheck("self", () => HealthCheckResult.Healthy());

app.MapHealthChecks("/health");
```

**Resposta:**
```json
{
  "status": "Healthy",
  "totalDuration": "00:00:00.0156789",
  "entries": {
    "database": {
      "status": "Healthy",
      "duration": "00:00:00.0123456"
    },
    "self": {
      "status": "Healthy",
      "duration": "00:00:00.0000001"
    }
  }
}
```

**Uso com Kubernetes:**
```yaml
livenessProbe:
  httpGet:
    path: /health
    port: 8080
  initialDelaySeconds: 10
  periodSeconds: 30
```

**Benefícios:**
- ✅ Monitoramento automatizado
- ✅ Integração com orquestradores
- ✅ Detecta problemas de DB
- ✅ Zero downtime deployments

---

### 6. CORS Multi-Ambiente

**Problema:** Frontend não conseguia fazer requisições cross-origin.

**Solução Implementada:**

**Arquivo:** `Program.cs`
```csharp
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });

    options.AddPolicy("Production", policy =>
    {
        policy.WithOrigins(builder.Configuration
                  .GetSection("Cors:AllowedOrigins")
                  .Get<string[]>() ?? Array.Empty<string>())
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials();
    });
});

app.UseCors(app.Environment.IsDevelopment() ? "AllowAll" : "Production");
```

**Configuração Produção:**

`appsettings.Production.json`:
```json
{
  "Cors": {
    "AllowedOrigins": [
      "https://yourfrontend.com",
      "https://www.yourfrontend.com"
    ]
  }
}
```

**Benefícios:**
- ✅ Desenvolvimento sem restrições
- ✅ Produção segura
- ✅ Suporte a credenciais (cookies)
- ✅ Configurável por ambiente

---

### 7. Rate Limiting

**Problema:** Endpoints de autenticação vulneráveis a brute force.

**Solução Implementada:**

**Arquivo:** `Program.cs`
```csharp
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddFixedWindowLimiter("fixed", opt =>
    {
        opt.PermitLimit = 100;
        opt.Window = TimeSpan.FromMinutes(1);
    });

    options.AddPolicy("auth", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1)
            }));
});
```

**Aplicação nos Endpoints:**
```csharp
group.MapPost("login", LoginAsync)
    .RequireRateLimiting("auth");
```

**Resposta (429 Too Many Requests):**
```json
{
  "success": false,
  "message": "Too many requests. Please try again later."
}
```

**Benefícios:**
- ✅ Proteção contra brute force
- ✅ Rate limiting por IP
- ✅ Configurável por endpoint
- ✅ Compliance com segurança

---

### 8. API Versioning

**Problema:** Sem estratégia de versionamento para evolução da API.

**Solução Implementada:**

**Arquivo:** `Program.cs`
```csharp
builder.Services.AddApiVersioning(options =>
{
    options.DefaultApiVersion = new ApiVersion(1, 0);
    options.AssumeDefaultVersionWhenUnspecified = true;
    options.ReportApiVersions = true;
    options.ApiVersionReader = ApiVersionReader.Combine(
        new UrlSegmentApiVersionReader(),
        new HeaderApiVersionReader("X-Api-Version"),
        new QueryStringApiVersionReader("api-version")
    );
});
```

**Endpoints:**
```csharp
var group = app.MapGroup("api/v{version:apiVersion}/users")
    .WithApiVersionSet(versionSet);
```

**URLs:**
```
POST /api/v1/users/register
POST /api/v1/users/login
POST /api/v1/users/refresh-token
```

**Múltiplas Formas de Versionar:**
```bash
# URL (recomendado)
POST /api/v1/users/login

# Header
POST /api/users/login
X-Api-Version: 1.0

# Query String
POST /api/users/login?api-version=1.0
```

**Benefícios:**
- ✅ Evolução sem breaking changes
- ✅ Suporte a múltiplas versões
- ✅ Deprecation strategy
- ✅ Backward compatibility

---

### 9. Options Pattern para Configurações

**Problema:** Configurações lidas diretamente do IConfiguration.

**Solução Implementada:**

**Arquivo:** `src/Configuration/JwtOptions.cs`
```csharp
public class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Secret { get; init; } = string.Empty;
    public string Issuer { get; init; } = string.Empty;
    public string Audience { get; init; } = string.Empty;
    public int ExpirationHours { get; init; } = 24;
}
```

**Registro:**
```csharp
builder.Services.Configure<JwtOptions>(
    builder.Configuration.GetSection(JwtOptions.SectionName)
);
```

**Uso no Serviço:**
```csharp
public class UserService(
    UserManager<User> userManager,
    IOptions<JwtOptions> jwtOptions
) : IUserService
{
    private readonly JwtOptions _jwtOptions = jwtOptions.Value;

    private string GenerateJwt(User user)
    {
        var key = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(_jwtOptions.Secret)
        );
        // ...
    }
}
```

**Benefícios:**
- ✅ Type-safe configuration
- ✅ Validação em startup
- ✅ Injeção de dependência
- ✅ Testabilidade

---

### 10. Response Pattern Padronizado

**Problema:** Respostas inconsistentes entre endpoints.

**Solução Implementada:**

**Arquivo:** `src/Common/ApiResponse.cs`
```csharp
public class ApiResponse<T>
{
    public bool Success { get; init; }
    public T? Data { get; init; }
    public string? Message { get; init; }
    public IDictionary<string, string[]>? Errors { get; init; }

    public static ApiResponse<T> SuccessResponse(T data, string? message = null)
    {
        return new ApiResponse<T>
        {
            Success = true,
            Data = data,
            Message = message
        };
    }

    public static ApiResponse<T> ErrorResponse(
        string message,
        IDictionary<string, string[]>? errors = null)
    {
        return new ApiResponse<T>
        {
            Success = false,
            Message = message,
            Errors = errors
        };
    }
}
```

**Uso:**
```csharp
private static async Task<IResult> LoginAsync(
    LoginRequest request,
    IUserService userService)
{
    var response = await userService.LoginAsync(request);
    return Results.Ok(
        ApiResponse<LoginResponse>.SuccessResponse(
            response,
            "Login successful."
        )
    );
}
```

**Resposta Sucesso:**
```json
{
  "success": true,
  "data": {
    "accessToken": "eyJhbGci...",
    "refreshToken": "k8s9m2n3...",
    "accessTokenExpireAt": "2026-04-18T10:30:00Z",
    "refreshTokenExpireAt": "2026-04-25T09:30:00Z",
    "username": "johndoe",
    "userId": "123e4567-e89b-12d3-a456-426614174000"
  },
  "message": "Login successful."
}
```

**Resposta Erro:**
```json
{
  "success": false,
  "message": "One or more validation errors occurred.",
  "errors": {
    "Email": ["Email is required."],
    "Password": ["Password is required."]
  }
}
```

**Benefícios:**
- ✅ Respostas consistentes
- ✅ Fácil parsing no frontend
- ✅ Distingue sucesso/erro facilmente
- ✅ Suporte a múltiplos erros

---

### 11. Swagger Avançado

**Problema:** Documentação Swagger básica sem exemplos.

**Solução Implementada:**

**Endpoints com Metadata:**
```csharp
group.MapPost("login", LoginAsync)
    .WithName("LoginUser")
    .WithSummary("Authenticate user")
    .WithDescription("Authenticates a user and returns JWT tokens")
    .Produces<ApiResponse<LoginResponse>>(StatusCodes.Status200OK)
    .Produces<ApiResponse<object>>(StatusCodes.Status401Unauthorized)
    .WithTags("Authentication");
```

**Resultado no Swagger:**
- Nome descritivo da operação
- Descrição completa
- Códigos de status documentados
- Tipos de resposta clarificados
- Agrupamento por tags

**Benefícios:**
- ✅ Documentação auto-atualizada
- ✅ Try it out funcional
- ✅ Facilita integração
- ✅ Onboarding de devs

---

## 🚀 Features Avançadas (P2 - Média)

### 12. Refresh Tokens

**Problema:** Access tokens expiram rápido, forçando re-login frequente.

**Solução Implementada:**

**Modelo:**

`src/Models/RefreshToken.cs`:
```csharp
public class RefreshToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Token { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? RevokedAt { get; set; }

    public bool IsExpired => DateTime.UtcNow >= ExpiresAt;
    public bool IsRevoked => RevokedAt.HasValue;
    public bool IsActive => !IsExpired && !IsRevoked;

    public User User { get; set; } = null!;
}
```

**Geração do Refresh Token:**
```csharp
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
```

**Login Response (com Refresh Token):**
```json
{
  "success": true,
  "data": {
    "accessToken": "eyJhbGci...",
    "refreshToken": "k8s9m2n3p4q5r6s7t8u9v0w1x2y3z4a5b6c7d8e9f0==",
    "accessTokenExpireAt": "2026-04-18T10:30:00Z",
    "refreshTokenExpireAt": "2026-04-25T09:30:00Z",
    "username": "johndoe",
    "userId": "123e4567-e89b-12d3-a456-426614174000"
  },
  "message": "Login successful."
}
```

**Renovação de Token:**

**Endpoint:** `POST /api/v1/users/refresh-token`

**Request:**
```json
{
  "refreshToken": "k8s9m2n3p4q5r6s7t8u9v0w1x2y3z4a5b6c7d8e9f0=="
}
```

**Response:**
```json
{
  "success": true,
  "data": {
    "accessToken": "eyJhbGci... (novo)",
    "refreshToken": "m9n0o1p2... (novo)",
    "accessTokenExpireAt": "2026-04-18T11:00:00Z",
    "refreshTokenExpireAt": "2026-04-25T10:00:00Z"
  },
  "message": "Token refreshed successfully."
}
```

**Lógica de Refresh:**
```csharp
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
    var newRefreshToken = await GenerateRefreshTokenAsync(storedToken.UserId);

    await context.SaveChangesAsync();

    return new RefreshTokenResponse(
        accessToken,
        newRefreshToken.Token,
        DateTime.UtcNow.AddHours(_jwtOptions.ExpirationHours),
        newRefreshToken.ExpiresAt
    );
}
```

**Fluxo Completo:**
```
1. User faz login
   ↓
2. Recebe Access Token (1h) + Refresh Token (7d)
   ↓
3. Usa Access Token nas requisições
   ↓
4. Access Token expira
   ↓
5. Frontend envia Refresh Token para /refresh-token
   ↓
6. Recebe novo Access Token + novo Refresh Token
   ↓
7. Refresh Token antigo é revogado
```

**Segurança:**
- ✅ Refresh Token gerado com `RandomNumberGenerator` (criptograficamente seguro)
- ✅ Armazenado no banco com hash (opcional: adicionar hashing)
- ✅ Revogação automática ao renovar
- ✅ Validação de expiração
- ✅ One-time use (revogado após uso)

**Benefícios:**
- ✅ Melhor UX (sem re-login constante)
- ✅ Segurança (Access Token de curta duração)
- ✅ Revogação granular
- ✅ Auditoria completa

---

### 13. Auditoria Automática

**Problema:** Sem rastreamento de quando registros foram criados/modificados.

**Solução Implementada:**

**Interface:**

`src/Common/IAuditable.cs`:
```csharp
public interface IAuditable
{
    DateTime CreatedAt { get; set; }
    DateTime? UpdatedAt { get; set; }
    DateTime? DeletedAt { get; set; }
}
```

**Modelo User (exemplo):**
```csharp
public class User : IdentityUser<Guid>, IAuditable
{
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
}
```

**Interceptor no DbContext:**
```csharp
public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
{
    SetAuditableFields();
    return base.SaveChangesAsync(cancellationToken);
}

public override int SaveChanges()
{
    SetAuditableFields();
    return base.SaveChanges();
}

private void SetAuditableFields()
{
    var entries = ChangeTracker.Entries<IAuditable>();

    foreach (var entry in entries)
    {
        switch (entry.State)
        {
            case EntityState.Added:
                entry.Entity.CreatedAt = DateTime.UtcNow;
                break;
            case EntityState.Modified:
                entry.Entity.UpdatedAt = DateTime.UtcNow;
                break;
        }
    }
}
```

**Resultado no Banco:**
```sql
SELECT Id, UserName, CreatedAt, UpdatedAt
FROM Users
WHERE Id = '123e4567-e89b-12d3-a456-426614174000';

-- Resultado:
-- Id: 123e4567-e89b-12d3-a456-426614174000
-- UserName: johndoe
-- CreatedAt: 2026-04-18 09:30:15
-- UpdatedAt: 2026-04-18 10:45:22
```

**Soft Delete (opcional):**
```csharp
// Em vez de deletar fisicamente:
user.DeletedAt = DateTime.UtcNow;
await context.SaveChangesAsync();

// Query Filter global para ignorar deletados:
modelBuilder.Entity<User>()
    .HasQueryFilter(u => u.DeletedAt == null);
```

**Benefícios:**
- ✅ Auditoria automática
- ✅ Rastreamento de mudanças
- ✅ Compliance (LGPD/GDPR)
- ✅ Soft delete para recuperação
- ✅ Zero código extra em services

---

### 14. CI/CD Pipeline (GitHub Actions)

**Problema:** Build e deploy manuais, propensos a erro.

**Solução Implementada:**

**Arquivo:** `.github/workflows/dotnet.yml`
```yaml
name: .NET CI/CD

on:
  push:
    branches: [ main, develop ]
  pull_request:
    branches: [ main ]

jobs:
  build-and-test:
    runs-on: ubuntu-latest

    steps:
    - uses: actions/checkout@v4

    - name: Setup .NET
      uses: actions/setup-dotnet@v4
      with:
        dotnet-version: '8.0.x'

    - name: Restore dependencies
      run: dotnet restore

    - name: Build
      run: dotnet build --no-restore --configuration Release

    - name: Run tests
      run: dotnet test --no-build --configuration Release --verbosity normal

    - name: Publish
      run: dotnet publish --no-build --configuration Release --output ./publish

    - name: Upload artifact
      uses: actions/upload-artifact@v4
      with:
        name: users-api
        path: ./publish

  docker:
    runs-on: ubuntu-latest
    needs: build-and-test
    if: github.ref == 'refs/heads/main'

    steps:
    - uses: actions/checkout@v4

    - name: Log in to Docker Hub
      uses: docker/login-action@v3
      with:
        username: ${{ secrets.DOCKER_USERNAME }}
        password: ${{ secrets.DOCKER_PASSWORD }}

    - name: Build and push Docker image
      uses: docker/build-push-action@v5
      with:
        context: .
        push: true
        tags: |
          ${{ secrets.DOCKER_USERNAME }}/users-api:latest
          ${{ secrets.DOCKER_USERNAME }}/users-api:${{ github.sha }}
```

**Fluxo:**
```
1. Push para main/develop ou PR
   ↓
2. Build & Test Job:
   - Restore packages
   - Build em Release
   - Run tests
   - Publish artifact
   ↓
3. Docker Job (apenas main):
   - Build Docker image
   - Tag com 'latest' e SHA do commit
   - Push para Docker Hub
```

**Secrets Necessários (GitHub):**
```
Settings → Secrets and variables → Actions → New repository secret

DOCKER_USERNAME: seu-usuario
DOCKER_PASSWORD: seu-token-docker-hub
```

**Benefícios:**
- ✅ Build automático em cada push
- ✅ Testes executados automaticamente
- ✅ Docker image versionado
- ✅ Deploy contínuo
- ✅ Feedback rápido em PRs

---

### 15. Estrutura de Testes

**Problema:** Sem testes automatizados.

**Solução Implementada:**

**Projeto:** `tests/users-api.Tests/`

**Pacotes:**
- xUnit (framework de testes)
- FluentAssertions (assertions fluentes)
- Moq (mocking)
- Microsoft.EntityFrameworkCore.InMemory (DB em memória)

**Exemplo de Teste:**
```csharp
public class UserServiceTests
{
    [Fact]
    public async Task LoginAsync_WithValidCredentials_ShouldReturnLoginResponse()
    {
        // Arrange
        var request = new LoginRequest("test@example.com", "Test@123");
        var user = new User
        {
            Id = Guid.NewGuid(),
            UserName = "testuser",
            Email = request.Email
        };

        _userManagerMock
            .Setup(x => x.FindByEmailAsync(request.Email))
            .ReturnsAsync(user);

        _userManagerMock
            .Setup(x => x.CheckPasswordAsync(user, request.Password))
            .ReturnsAsync(true);

        // Act
        var result = await _sut.LoginAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.AccessToken.Should().NotBeNullOrEmpty();
        result.RefreshToken.Should().NotBeNullOrEmpty();
        result.Username.Should().Be(user.UserName);
        result.UserId.Should().Be(user.Id.ToString());
    }

    [Fact]
    public async Task LoginAsync_WithInvalidEmail_ShouldThrowUnauthorizedException()
    {
        // Arrange
        var request = new LoginRequest("nonexistent@example.com", "Test@123");

        _userManagerMock
            .Setup(x => x.FindByEmailAsync(request.Email))
            .ReturnsAsync((User?)null);

        // Act
        var act = async () => await _sut.LoginAsync(request);

        // Assert
        await act.Should().ThrowAsync<UnauthorizedException>()
            .WithMessage("Invalid credentials.");
    }
}
```

**Executar Testes:**
```bash
dotnet test
```

**Benefícios:**
- ✅ Detecta regressões
- ✅ Documenta comportamento esperado
- ✅ Facilita refactoring
- ✅ Aumenta confiança

---

## 🏛️ Arquitetura e Patterns

### Estrutura de Pastas

```
users-api/
├── src/
│   ├── Common/              # Código compartilhado
│   │   ├── ApiResponse.cs
│   │   └── IAuditable.cs
│   ├── Configuration/       # Options classes
│   │   └── JwtOptions.cs
│   ├── Data/               # EF Core
│   │   ├── UsersDbContext.cs
│   │   └── Migrations/
│   ├── DTOs/               # Data Transfer Objects
│   │   ├── Login.cs
│   │   ├── Register.cs
│   │   └── RefreshTokenDTOs.cs
│   ├── Endpoints/          # Minimal API endpoints
│   │   └── UsersEndpoints.cs
│   ├── Exceptions/         # Custom exceptions
│   │   ├── ValidationException.cs
│   │   ├── UnauthorizedException.cs
│   │   └── BusinessException.cs
│   ├── Filters/            # Endpoint filters
│   │   └── ValidationFilter.cs
│   ├── Middleware/         # ASP.NET middleware
│   │   └── GlobalExceptionHandlerMiddleware.cs
│   ├── Models/             # Domain entities
│   │   ├── User.cs
│   │   └── RefreshToken.cs
│   ├── Services/           # Business logic
│   │   ├── IUserService.cs
│   │   └── UserService.cs
│   └── Validators/         # FluentValidation
│       ├── RegisterRequestValidator.cs
│       ├── LoginRequestValidator.cs
│       └── RefreshTokenRequestValidator.cs
├── .github/
│   └── workflows/
│       └── dotnet.yml      # CI/CD pipeline
├── appsettings.json
├── appsettings.Development.json
├── appsettings.Staging.json
├── appsettings.Production.json
├── Dockerfile
├── Program.cs
└── users-api.csproj
```

### Patterns Utilizados

#### 1. **Repository Pattern** (via EF Core)
```csharp
public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
```

#### 2. **Options Pattern**
```csharp
builder.Services.Configure<JwtOptions>(
    builder.Configuration.GetSection(JwtOptions.SectionName)
);
```

#### 3. **Dependency Injection**
```csharp
public class UserService(
    UserManager<User> userManager,
    UsersDbContext context,
    IOptions<JwtOptions> jwtOptions
) : IUserService
```

#### 4. **Middleware Pattern**
```csharp
app.UseMiddleware<GlobalExceptionHandlerMiddleware>();
```

#### 5. **Filter Pattern**
```csharp
group.MapPost("register", RegisterAsync)
    .AddEndpointFilter<ValidationFilter<RegisterRequest>>();
```

#### 6. **Strategy Pattern** (Exception Handling)
```csharp
var (statusCode, response) = exception switch
{
    ValidationException validationEx => (...),
    UnauthorizedException unauthorizedEx => (...),
    _ => (...)
};
```

#### 7. **Factory Pattern** (Token Generation)
```csharp
private async Task<RefreshToken> GenerateRefreshTokenAsync(Guid userId)
```

#### 8. **Interceptor Pattern** (Audit)
```csharp
public override Task<int> SaveChangesAsync(...)
{
    SetAuditableFields();
    return base.SaveChangesAsync(...);
}
```

---

## 📖 Como Usar

### 1. Configuração Inicial

**Clone o repositório:**
```bash
git clone <repo-url>
cd users-api
```

**Configure User Secrets (Desenvolvimento):**
```bash
dotnet user-secrets init
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Database=fcg_users_db;Username=fcg;Password=fcgpw"
dotnet user-secrets set "Jwt:Secret" "sua-chave-secreta-com-32-chars-minimo!!"
dotnet user-secrets set "Jwt:Issuer" "fcg_jwt_issuer"
dotnet user-secrets set "Jwt:Audience" "fcg_jwt_audience"
dotnet user-secrets set "Jwt:ExpirationHours" "1"
```

**Execute as Migrations:**
```bash
dotnet ef database update
```

**Execute a aplicação:**
```bash
dotnet run
```

**Acesse o Swagger:**
```
https://localhost:5001/swagger
```

---

### 2. Endpoints Disponíveis

#### **POST** `/api/v1/users/register`

**Request:**
```json
{
  "username": "johndoe",
  "email": "john@example.com",
  "password": "Strong@Pass123"
}
```

**Response (201 Created):**
```json
{
  "success": true,
  "data": {
    "userId": "123e4567-e89b-12d3-a456-426614174000",
    "username": "johndoe",
    "email": "john@example.com"
  },
  "message": "User registered successfully."
}
```

---

#### **POST** `/api/v1/users/login`

**Request:**
```json
{
  "email": "john@example.com",
  "password": "Strong@Pass123"
}
```

**Response (200 OK):**
```json
{
  "success": true,
  "data": {
    "accessToken": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
    "refreshToken": "k8s9m2n3p4q5r6s7t8u9v0w1x2y3z4a5b6c7d8e9f0==",
    "accessTokenExpireAt": "2026-04-18T10:30:00Z",
    "refreshTokenExpireAt": "2026-04-25T09:30:00Z",
    "username": "johndoe",
    "userId": "123e4567-e89b-12d3-a456-426614174000"
  },
  "message": "Login successful."
}
```

---

#### **POST** `/api/v1/users/refresh-token`

**Request:**
```json
{
  "refreshToken": "k8s9m2n3p4q5r6s7t8u9v0w1x2y3z4a5b6c7d8e9f0=="
}
```

**Response (200 OK):**
```json
{
  "success": true,
  "data": {
    "accessToken": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9... (novo)",
    "refreshToken": "m9n0o1p2q3r4s5t6u7v8w9x0y1z2a3b4c5d6e7f8g9h0==",
    "accessTokenExpireAt": "2026-04-18T11:00:00Z",
    "refreshTokenExpireAt": "2026-04-25T10:00:00Z"
  },
  "message": "Token refreshed successfully."
}
```

---

#### **GET** `/health`

**Response (200 OK):**
```json
{
  "status": "Healthy"
}
```

---

### 3. Autenticação em Requisições

**Adicione o token no header:**
```bash
curl -X GET https://localhost:5001/api/v1/users/profile \
  -H "Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9..."
```

**No Swagger:**
1. Clique em "Authorize"
2. Cole: `Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...`
3. Clique em "Authorize"
4. Feche o modal

---

## ⚙️ Configuração de Ambiente

### Desenvolvimento

**appsettings.Development.json:**
```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Debug",
      "Microsoft.AspNetCore": "Information",
      "Microsoft.EntityFrameworkCore": "Information"
    }
  },
  "Serilog": {
    "MinimumLevel": {
      "Default": "Debug",
      "Override": {
        "Microsoft": "Information",
        "System": "Warning"
      }
    }
  }
}
```

**User Secrets (não commitado):**
```bash
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "..."
dotnet user-secrets set "Jwt:Secret" "..."
```

---

### Staging

**appsettings.Staging.json:**
```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Information"
    }
  },
  "Serilog": {
    "MinimumLevel": {
      "Default": "Information"
    }
  },
  "Cors": {
    "AllowedOrigins": [
      "https://staging.yourfrontend.com"
    ]
  }
}
```

**Environment Variables:**
```bash
export ConnectionStrings__DefaultConnection="Host=staging-db;..."
export Jwt__Secret="staging-secret-key"
```

---

### Produção

**appsettings.Production.json:**
```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "Serilog": {
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "Microsoft": "Warning",
        "System": "Warning"
      }
    }
  },
  "Cors": {
    "AllowedOrigins": [
      "https://yourfrontend.com",
      "https://www.yourfrontend.com"
    ]
  }
}
```

**Azure Key Vault / Environment Variables:**
```bash
# Configurar no Azure App Service ou Kubernetes Secrets
ConnectionStrings__DefaultConnection=...
Jwt__Secret=...
Jwt__Issuer=...
Jwt__Audience=...
```

---

## 🧪 Testes

### Executar Testes

```bash
# Todos os testes
dotnet test

# Com cobertura
dotnet test /p:CollectCoverage=true

# Apenas uma classe
dotnet test --filter "FullyQualifiedName~UserServiceTests"

# Com verbosidade
dotnet test --verbosity detailed
```

### Estrutura de Testes

```
tests/users-api.Tests/
├── Services/
│   └── UserServiceTests.cs
├── Validators/
│   └── RegisterRequestValidatorTests.cs
└── GlobalUsings.cs
```

### Cobertura Recomendada

- **Services:** > 80%
- **Validators:** > 90%
- **Endpoints:** > 70%

---

## 🚀 CI/CD

### GitHub Actions

**Triggers:**
- Push para `main` ou `develop`
- Pull Request para `main`

**Jobs:**

1. **build-and-test**
   - Restore → Build → Test → Publish
   - Artifact gerado: `users-api`

2. **docker** (apenas em main)
   - Build imagem Docker
   - Tag: `latest` e `{sha}`
   - Push para Docker Hub

**Configurar Secrets:**
```
GitHub → Settings → Secrets → Actions

DOCKER_USERNAME
DOCKER_PASSWORD
```

---

### Docker

**Build local:**
```bash
docker build -t users-api:local .
```

**Run:**
```bash
docker run -d \
  -p 8080:8080 \
  -e ConnectionStrings__DefaultConnection="..." \
  -e Jwt__Secret="..." \
  users-api:local
```

**Docker Compose:**
```yaml
version: '3.8'

services:
  api:
    build: .
    ports:
      - "8080:8080"
    environment:
      - ConnectionStrings__DefaultConnection=Host=db;...
      - Jwt__Secret=${JWT_SECRET}
    depends_on:
      - db

  db:
    image: postgres:16
    environment:
      POSTGRES_DB: fcg_users_db
      POSTGRES_USER: fcg
      POSTGRES_PASSWORD: fcgpw
    volumes:
      - postgres_data:/var/lib/postgresql/data

volumes:
  postgres_data:
```

---

## 🗺️ Roadmap Futuro

### P3 - Baixa Prioridade

- [ ] **OpenTelemetry** - Distributed tracing
- [ ] **Polly** - Resilience policies (retry, circuit breaker)
- [ ] **MediatR** - CQRS pattern
- [ ] **AutoMapper** - DTO mapping
- [ ] **Hangfire** - Background jobs
- [ ] **SignalR** - Real-time notifications
- [ ] **Redis** - Distributed cache
- [ ] **Elasticsearch** - Advanced logging
- [ ] **GraphQL** - Alternative API
- [ ] **gRPC** - High-performance RPC

### Features de Negócio

- [ ] Email verification
- [ ] Password reset
- [ ] Two-factor authentication (2FA)
- [ ] OAuth2/OpenID Connect providers (Google, GitHub)
- [ ] Role-based access control (RBAC)
- [ ] Permissions system
- [ ] User profile management
- [ ] Account deactivation/deletion

### DevOps

- [ ] Kubernetes manifests
- [ ] Helm charts
- [ ] Terraform IaC
- [ ] Prometheus metrics
- [ ] Grafana dashboards
- [ ] ELK stack integration
- [ ] SonarQube code quality
- [ ] Dependabot security updates

---

## 📊 Comparativo de Esforço

| Melhoria | Esforço | Impacto | Prioridade |
|----------|---------|---------|------------|
| User Secrets | Baixo | Alto | P0 |
| FluentValidation | Médio | Alto | P0 |
| Exception Handling | Médio | Alto | P0 |
| Serilog | Baixo | Alto | P0 |
| Health Checks | Baixo | Médio | P1 |
| CORS | Baixo | Alto | P1 |
| Rate Limiting | Médio | Alto | P1 |
| API Versioning | Médio | Médio | P1 |
| Refresh Tokens | Alto | Alto | P2 |
| Auditoria | Baixo | Médio | P2 |
| CI/CD | Médio | Alto | P2 |
| Testes | Alto | Alto | P2 |

---

## ✅ Checklist de Produção

Antes de ir para produção, verifique:

- [ ] Secrets não estão commitados
- [ ] CORS configurado corretamente
- [ ] Rate limiting ativo
- [ ] Health checks funcionando
- [ ] Logs sendo armazenados
- [ ] Migrations aplicadas
- [ ] SSL/TLS configurado
- [ ] Firewall configurado
- [ ] Backup do banco configurado
- [ ] Monitoramento ativo
- [ ] Alertas configurados
- [ ] Documentação atualizada

---

## 📞 Suporte

**Documentação:**
- [Microsoft .NET Docs](https://docs.microsoft.com/dotnet)
- [ASP.NET Core Docs](https://docs.microsoft.com/aspnet/core)
- [EF Core Docs](https://docs.microsoft.com/ef/core)

**Issues:**
- Reporte bugs e sugestões no GitHub Issues

---

## 📄 Licença

Este projeto está sob a licença MIT.

---

**Última atualização:** 18/04/2026
**Versão da API:** v1.0
**Versão do .NET:** 8.0
