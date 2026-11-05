using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using Microsoft.EntityFrameworkCore;
using TutorHub.Controllers.Api;
using TutorHub.Data;
using TutorHub.Services;

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException(
        "Connection string 'Default' is not set. See README.md (user-secrets or ConnectionStrings__Default).");

builder.Services.AddDbContext<AppDbContext>(o => o.UseNpgsql(connectionString));
builder.Services.AddSingleton<IFileStorage, LocalFileStorage>();
builder.Services.AddScoped<MaterialService>();

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

app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();
