using System.Globalization;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.IdentityModel.Tokens;
using TutorHub.Controllers.Api;
using TutorHub.Data;
using TutorHub.Integrations;
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
builder.Services.AddSingleton<LessonNotificationInterceptor>();
builder.Services.AddSingleton<CrmSyncInterceptor>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<TenantContext>();

builder.Services.AddDbContext<AppDbContext>((services, o) => o
    .UseNpgsql(connectionString)
    .AddInterceptors(
        services.GetRequiredService<CacheInvalidationInterceptor>(),
        services.GetRequiredService<LessonNotificationInterceptor>(),
        services.GetRequiredService<CrmSyncInterceptor>())
    .ConfigureWarnings(w => w.Ignore(
        CoreEventId.PossibleIncorrectRequiredNavigationWithQueryFilterInteractionWarning,
        CoreEventId.RowLimitingOperationWithoutOrderByWarning)));
if (string.IsNullOrEmpty(builder.Configuration["Storage:Blob:ConnectionString"]))
{
    builder.Services.AddSingleton<IFileStorage, LocalFileStorage>();
}
else
{
    builder.Services.AddSingleton<IFileStorage, BlobFileStorage>();
}

builder.Services.AddScoped<MaterialService>();
builder.Services.AddScoped<JwtTokenService>();
builder.Services.AddScoped<TenantRegistration>();
builder.Services.AddScoped<FeatureFlags>();
builder.Services.AddScoped<TutorHub.Controllers.TutorPublishing>();
builder.Services.AddScoped<LearningAnalytics>();
builder.Services.AddScoped<TutorLocationService>();
builder.Services.AddScoped<LessonSummaryService>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<OutboxSignal>();
builder.Services.AddScoped<Outbox>();

var telegram = builder.Configuration.GetSection(TelegramSettings.Section).Get<TelegramSettings>() ?? new TelegramSettings();
var telegramMode = builder.Configuration.GetValue<TelegramMode?>("Telegram:Mode") ?? TelegramMode.Webhook;
builder.Services.AddSingleton(telegram);
builder.Services.AddHttpClient(TelegramBotClient.HttpClientName, client =>
    {
        client.BaseAddress = telegram.ApiBaseUrl ?? TelegramBotClient.BaseAddress;
        client.Timeout = TimeSpan.FromSeconds(TelegramPollingService.LongPollSeconds + 30);
    })
    .RemoveAllLoggers();
builder.Services.AddSingleton<TelegramClients>();
builder.Services.AddSingleton<ITelegramBotDirectory, TelegramBotDirectory>();
builder.Services.AddScoped<TelegramLinks>();
builder.Services.AddScoped<TelegramUpdateHandler>();
builder.Services.AddScoped<LessonReminders>();
builder.Services.AddDataProtection()
    .SetApplicationName("TutorHub")
    .PersistKeysToDbContext<AppDbContext>();
builder.Services.AddSingleton<HubSpotTokens>();
builder.Services.AddHttpClient<HubSpotClient>(HubSpotClient.HttpClientName, client =>
    {
        client.BaseAddress = HubSpotClient.BaseAddress;
        client.Timeout = TimeSpan.FromSeconds(30);
    })
    .RemoveAllLoggers();
builder.Services.AddScoped<CrmSync>();
if (telegram.HasBotToken)
{
    builder.Services.AddHostedService<LessonReminderService>();
}

var queueConnection = builder.Configuration[AzureMessageQueue.ConfigurationKey];
if (!string.IsNullOrEmpty(queueConnection))
{
    builder.Services.AddSingleton<IMessageQueue>(new AzureMessageQueue(queueConnection));
    builder.Services.AddScoped<OutboxProcessor>();
    builder.Services.AddHostedService<OutboxDispatcher>();
    builder.Services.AddHostedService<TelegramUpdateConsumer>();
    builder.Services.AddHostedService<CrmSyncConsumer>();
    if (telegram.HasBotToken && telegramMode == TelegramMode.Polling)
    {
        builder.Services.AddHostedService<TelegramPollingService>();
    }
}
builder.Services.AddSingleton(new NominatimThrottle(NominatimThrottle.PublicApiInterval));
builder.Services.AddHttpClient<IGeocoder, NominatimGeocoder>(client =>
{
    client.BaseAddress = NominatimGeocoder.BaseAddress;
    client.DefaultRequestHeaders.UserAgent.ParseAdd(NominatimGeocoder.UserAgent);
});

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
    .AddDefaultTokenProviders()
    .AddClaimsPrincipalFactory<AppUserClaimsFactory>();

builder.Services.AddAuthorizationBuilder()
    .AddPolicy(AppPolicies.Staff, p => p.RequireRole(TenantRoles.Owner, TenantRoles.Member))
    .AddPolicy(AppPolicies.OwnerOnly, p => p.RequireRole(TenantRoles.Owner));

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
builder.Services.AddHealthChecks().AddDbContextCheck<AppDbContext>();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
    await DbSeeder.SeedAsync(
        db,
        scope.ServiceProvider.GetRequiredService<IFileStorage>(),
        scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>(),
        app.Configuration);
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

app.MapHealthChecks("/healthz");
app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();

public partial class Program;
