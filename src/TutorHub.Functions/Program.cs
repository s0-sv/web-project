using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TutorHub.Integrations;

var builder = FunctionsApplication.CreateBuilder(args);

if (builder.Environment.IsDevelopment())
{
    builder.Configuration.AddUserSecrets(typeof(Program).Assembly, optional: true);
}

builder.ConfigureFunctionsWebApplication();

var telegram = builder.Configuration.GetSection(TelegramSettings.Section).Get<TelegramSettings>() ?? new TelegramSettings();
builder.Services.AddSingleton(telegram);
builder.Services.AddHttpClient<TelegramBotClient>(TelegramBotClient.HttpClientName, client =>
    {
        client.BaseAddress = telegram.ApiBaseUrl ?? TelegramBotClient.BaseAddress;
        client.Timeout = TimeSpan.FromSeconds(30);
    })
    .RemoveAllLoggers();

builder.Build().Run();

public partial class Program;
