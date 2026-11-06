using System.Globalization;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using TutorHub.Controllers.Api;
using TutorHub.Data;
using TutorHub.Models;
using TutorHub.Services;

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException(
        "Connection string 'Default' is not set. Use user-secrets or the ConnectionStrings__Default variable.");

builder.Services.AddOutputCache(o => o.AddPolicy(
    CacheInvalidationInterceptor.PolicyName,
    new UserOutputCachePolicy()));
builder.Services.AddSingleton<CacheInvalidationInterceptor>();

builder.Services.AddDbContext<AppDbContext>((services, o) => o
    .UseNpgsql(connectionString)
    .AddInterceptors(services.GetRequiredService<CacheInvalidationInterceptor>()));
builder.Services.AddSingleton<IFileStorage, LocalFileStorage>();
builder.Services.AddScoped<MaterialService>();
builder.Services.AddScoped<JwtTokenService>();

builder.Services.AddIdentityCore<ApplicationUser>(o =>
    {
        o.User.RequireUniqueEmail = true;
        o.Password.RequiredLength = 8;
        o.Password.RequireDigit = true;
        o.Password.RequireLowercase = false;
        o.Password.RequireUppercase = false;
        o.Password.RequireNonAlphanumeric = false;
        o.Lockout.AllowedForNewUsers = true;
        o.Lockout.MaxFailedAccessAttempts = 5;
        o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    })
    .AddEntityFrameworkStores<AppDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

var authentication = builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme);
authentication.AddIdentityCookies();
authentication.AddJwtBearer(o =>
{
    o.MapInboundClaims = false;
    o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidIssuer = JwtTokenService.Issuer,
        ValidAudience = JwtTokenService.Audience,
        IssuerSigningKey = JwtTokenService.SigningKey(builder.Configuration),
        ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
        NameClaimType = ClaimTypes.Name,
        RoleClaimType = ClaimTypes.Role
    };
});

var googleClientId = builder.Configuration["Authentication:Google:ClientId"];
var googleClientSecret = builder.Configuration["Authentication:Google:ClientSecret"];
if (!string.IsNullOrEmpty(googleClientId) && !string.IsNullOrEmpty(googleClientSecret))
{
    authentication.AddGoogle(o =>
    {
        o.ClientId = googleClientId;
        o.ClientSecret = googleClientSecret;
        o.SignInScheme = IdentityConstants.ExternalScheme;
    });
}

builder.Services.ConfigureApplicationCookie(o =>
{
    o.LoginPath = "/Account/Login";
    o.AccessDeniedPath = "/Account/AccessDenied";
    o.Cookie.HttpOnly = true;
    o.Cookie.SameSite = SameSiteMode.Lax;
    o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
});

builder.Services.AddControllersWithViews()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(
        new JsonStringEnumConverter(allowIntegerValues: false)));

builder.Services.AddWebEncoders(o =>
    o.TextEncoderSettings = new TextEncoderSettings(UnicodeRanges.All));

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
    await DbSeeder.SeedAsync(db, scope.ServiceProvider.GetRequiredService<IFileStorage>());
}

static bool IsApi(HttpContext context) => context.Request.Path.StartsWithSegments("/api");

app.UseWhen(IsApi, api =>
{
    api.UseExceptionHandler();
    api.UseStatusCodePages();
});

app.UseWhen(context => !IsApi(context), mvc =>
{
    if (!app.Environment.IsDevelopment())
    {
        mvc.UseExceptionHandler("/Home/Error");
        mvc.UseHsts();
    }

    mvc.UseStatusCodePagesWithReExecute("/Home/Status/{0}");
});

app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();
app.UseOutputCache();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();

public partial class Program;
