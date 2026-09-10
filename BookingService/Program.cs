using System.Security.Claims;
using System.Text;
using AuthGrpcService;
using BookingService.Contracts;
using BookingService.Data;
using BookingService.Endpoints;
using BookingService.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<BookingDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

var jwtSettings = builder.Configuration.GetSection("JwtSettings").Get<JwtSettings>() ?? new JwtSettings();

if (string.IsNullOrWhiteSpace(jwtSettings.Key))
{
    throw new InvalidOperationException(
        "JwtSettings:Key is missing. Set JwtSettings__Key - see .env.example.");
}

// The token is signed by AuthService and verified here with the same key.
// No call back to AuthService per request: a JWT already proves itself.
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtSettings.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtSettings.Audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.Key)),
        };
    });

builder.Services.AddAuthorization();

// AuthService is reached only to issue tokens (login / register).
builder.Services.AddGrpcClient<Auth.AuthClient>(options =>
{
    var address = builder.Configuration["AuthService:Address"]
                  ?? throw new InvalidOperationException("AuthService:Address is missing.");
    options.Address = new Uri(address);
});

builder.Services.AddOpenApi();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<BookingDbContext>();
    db.Database.Migrate();
}

app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.MapAuthEndpoints();
app.MapRoomEndpoints();
app.MapBookingEndpoints();

app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));

app.Run();

