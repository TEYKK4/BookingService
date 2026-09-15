using System.Text;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using RoomBooking.Contracts;
using RoomBooking.Data;
using RoomBooking.Endpoints;
using RoomBooking.Models;
using RoomBooking.Services;
using RoomBooking.Validators;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

var jwtSection = builder.Configuration.GetSection("JwtSettings");
var jwtSettings = jwtSection.Get<JwtSettings>() ?? new JwtSettings();

// HMAC-SHA256 needs a key of at least 256 bits. Anything shorter fails deep inside
// the signing call with an unhelpful message, so check it here, up front.
if (Encoding.UTF8.GetByteCount(jwtSettings.Key) < 32)
{
    throw new InvalidOperationException(
        "JwtSettings:Key must be at least 32 bytes. Set JwtSettings__Key - see .env.example.");
}

builder.Services.Configure<JwtSettings>(jwtSection);
builder.Services.AddScoped<JwtTokenService>();
builder.Services.AddScoped<IValidator<CredentialsRequest>, CredentialsValidator>();

// The same key signs tokens in /api/auth and verifies them everywhere else.
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

builder.Services.AddOpenApi();

// Unhandled exceptions become a JSON problem response instead of an empty 500.
builder.Services.AddProblemDetails();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();

    // Fail at startup, not on the first booking, if any room's zone cannot be
    // resolved - e.g. a typo in seed data, or a runtime image without tzdata.
    foreach (var timeZoneId in db.Rooms.Select(r => r.TimeZoneId).Distinct())
    {
        BookingHours.ZoneOf(timeZoneId);
    }
}

app.UseExceptionHandler();

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

// Lets the test project point WebApplicationFactory at this assembly.
public partial class Program;
