using System.Text;
using AuthGrpcService;
using AuthGrpcService.Data;
using AuthGrpcService.Interceptors;
using AuthGrpcService.Models;
using AuthGrpcService.Services;
using AuthGrpcService.Validators;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

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

builder.Services.AddScoped<IValidator<Credentials>, CredentialsValidator>();
builder.Services.AddScoped<ValidationInterceptor>();

builder.Services.AddGrpc(options =>
{
    options.Interceptors.Add<ValidationInterceptor>();
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}

// This service only issues tokens; nothing here requires one. Token validation
// lives in BookingService, which is the only place that consumes them.
app.MapGrpcService<AuthService>();
app.MapGet("/",
    () =>
        "Communication with gRPC endpoints must be made through a gRPC client. To learn how to create a client, visit: https://go.microsoft.com/fwlink/?linkid=2086909");

app.Run();
