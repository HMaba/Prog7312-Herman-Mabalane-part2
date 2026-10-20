using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using SmartXApi.Data;
using SmartXApi.Security;
using SmartXApi.Services;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

var firebaseProjectId = builder.Configuration["Firebase:ProjectId"];
if (string.IsNullOrWhiteSpace(firebaseProjectId))
{
    throw new InvalidOperationException("Firebase:ProjectId is not configured. Set it with: dotnet user-secrets set \"Firebase:smar-auth\" \"<your-firebase-project-id>\"");
}

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("ConnectionStrings:DefaultConnection is not configured.");
}

builder.Services.AddControllers().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
});

builder.Services.AddOpenApi();
builder.Services.AddSwaggerGen();
builder.Services.AddEndpointsApiExplorer();

builder.Services.AddDbContext<SmartXDbContext>(options =>
{
    options.UseSqlServer(connectionString);
});

var issuer = $"https://securetoken.google.com/{firebaseProjectId}";

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
    {
        options.Authority = issuer;
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = issuer,
            ValidateAudience = true,
            ValidAudience = firebaseProjectId,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    })
    .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, DeviceKeyAuthenticationHandler>(
        DeviceKeyDefaults.Scheme, _ => { });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(DeviceKeyDefaults.IngestPolicy, policy => policy
        .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme, DeviceKeyDefaults.Scheme)
        .RequireAuthenticatedUser());
});

builder.Services.AddSingleton<StreamDataStructures>();
builder.Services.AddSingleton<PredictiveActionEngine>();
builder.Services.AddSingleton<ITelemetryStreamProcessor, TelemetryStreamProcessor>();
builder.Services.AddHostedService<StreamWorker>();

builder.Services.AddScoped<IDeviceService, DeviceService>();
builder.Services.AddScoped<ISensorService, SensorService>();
builder.Services.AddScoped<IAttachmentService, AttachmentService>();
builder.Services.AddScoped<ITelemetryValidationService, TelemetryValidationService>();
builder.Services.AddScoped<ITelemetryProcessingService, TelemetryProcessingService>();
builder.Services.AddScoped<ITelemetryService, TelemetryService>();
builder.Services.AddScoped<IDeploymentValidationService, DeploymentValidationService>();
builder.Services.AddScoped<ICommandStreamService, CommandStreamService>();
builder.Services.AddScoped<IDeviceImportService, DeviceImportService>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<SmartXDbContext>();
    context.Database.EnsureCreated();
    DataSeeder.Seed(context);
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
