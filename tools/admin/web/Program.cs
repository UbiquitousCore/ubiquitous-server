using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;
using Ubiquitous.Admin;
using Ubiquitous.Admin.Stores;
using Ubiquitous.Admin.Web;

var options = AdminOptions.FromEnvironment(Environment.GetEnvironmentVariable);
var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(options.Urls);

await using var connection = await ServerConnection.OpenAsync(
    options.PostgresConnectionString, options.PostgresPasswordFile, options.StateDirectory);

// Own choice: a stuck game server should show as unreachable within seconds, not hold the page.
builder.Services.AddHttpClient(Options.DefaultName, http => http.Timeout = TimeSpan.FromSeconds(3));
builder.Services.AddSingleton<IAccountDirectory>(new ServerAccounts(connection.Accounts));
var items = new PackageItems(options.DataDirectory);
builder.Services.AddSingleton<IItemCatalog>(items);
builder.Services.AddSingleton<ICharacterDirectory>(new ServerCharacters(connection.Characters, connection.Inventory, items));
builder.Services.AddSingleton<IOnlinePlayers>(services =>
    new ServerOnline(services.GetRequiredService<IHttpClientFactory>().CreateClient(), new Uri(options.ServerUrl, "online"), options.AdminTokenFile));
builder.Services.AddSingleton<ISettingsStore>(new EnvFileSettings(Path.GetFullPath(options.SettingsFile)));
builder.Services.AddSingleton<IServerStatus>(services =>
    new HealthProbe(services.GetRequiredService<IHttpClientFactory>().CreateClient(), new Uri(options.ServerUrl, "health")));
builder.Services.AddSingleton<IServerControl>(services =>
    new ServerRestart(services.GetRequiredService<IHttpClientFactory>().CreateClient(), new Uri(options.ServerUrl, "restart"), options.AdminTokenFile));
builder.Services.AddSingleton<AdminService>();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(cookie =>
{
    cookie.Cookie.Name = "ubiquitous-admin";
    cookie.Cookie.SameSite = SameSiteMode.Strict;
    cookie.Cookie.HttpOnly = true;
    cookie.ExpireTimeSpan = TimeSpan.FromHours(8);
    cookie.Events.OnRedirectToLogin = context =>
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    };
});
builder.Services.AddAuthorization();

var app = builder.Build();
if (connection.UsesFiles)
    app.Logger.LogWarning("Using the JSON saves in {Directory}. Stop the game server before changing them here, or it may write over the change.", options.StateDirectory);
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
AdminApi.Map(app);
app.Logger.LogInformation("Admin page on {Urls}", options.Urls);
await app.RunAsync();

public partial class Program;
