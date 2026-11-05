using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Ubiquitous.Admin.Web;

public sealed record SignInRequest(string UserId, string Password);

public sealed record NewAccountRequest(string UserId, string Password);

public sealed record PasswordRequest(string Password);

public sealed record GmRequest(bool IsGm);

public sealed record SettingsRequest(Dictionary<string, string?> Values);

public sealed record CharacterEditRequest(string? Name, int? Level, int? Gold);

public sealed record GiveItemRequest(int Entry, int Count);

public static class AdminApi
{
    // Every request that changes something carries this header. A page on
    // another site cannot add it without the browser asking this one first.
    public const string RequestHeader = "X-Ubiquitous-Admin";

    public static void Map(WebApplication app)
    {
        var api = app.MapGroup("/api");
        api.AddEndpointFilter(async (context, next) =>
        {
            var request = context.HttpContext.Request;
            if (!HttpMethods.IsGet(request.Method) && !request.Headers.ContainsKey(RequestHeader))
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            return await next(context);
        });

        api.MapPost("/session", async (SignInRequest body, AdminService admin, HttpContext http, CancellationToken token) =>
        {
            if (await admin.SignInAsync(body.UserId, body.Password, token) is not { } account)
                return Results.Json(new { error = "Wrong user id or password, or the account is not a GM." }, statusCode: 401);
            var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, account.UserId)], CookieAuthenticationDefaults.AuthenticationScheme);
            await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
            return Results.Ok(new { userId = account.UserId });
        });

        api.MapDelete("/session", async (HttpContext http) =>
        {
            await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.NoContent();
        });

        var gm = api.MapGroup("").RequireAuthorization();
        gm.AddEndpointFilter(async (context, next) =>
        {
            var admin = context.HttpContext.RequestServices.GetRequiredService<AdminService>();
            var userId = context.HttpContext.User.Identity?.Name;
            if (userId is null || !await admin.IsStillGmAsync(userId, context.HttpContext.RequestAborted))
            {
                await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                return Results.Unauthorized();
            }

            return await next(context);
        });

        gm.MapGet("/me", (HttpContext http) => Results.Ok(new { userId = http.User.Identity!.Name }));

        gm.MapGet("/status", async (AdminService admin, CancellationToken token) =>
        {
            var status = await admin.StatusAsync(token);
            return Results.Ok(new { state = status.State.ToString(), reason = status.Reason, settingsFile = admin.SettingsLocation });
        });

        gm.MapPost("/server/restart", async (AdminService admin, CancellationToken token) =>
            await admin.RestartServerAsync(token)
                ? Results.Accepted()
                : Results.Json(new { error = "The server could not be asked to restart. Is it running, with the admin token set up?" }, statusCode: 502));

        gm.MapGet("/accounts", async (AdminService admin, CancellationToken token) => Results.Ok(await admin.AccountsAsync(token)));

        gm.MapPost("/accounts", async (NewAccountRequest body, AdminService admin, CancellationToken token) =>
            Answer(await admin.CreateAccountAsync(body.UserId, body.Password, token)));

        gm.MapPost("/accounts/{id:int}/password", async (int id, PasswordRequest body, AdminService admin, CancellationToken token) =>
            Answer(await admin.SetPasswordAsync(id, body.Password, token)));

        gm.MapPost("/accounts/{id:int}/gm", async (int id, GmRequest body, AdminService admin, CancellationToken token) =>
            Answer(await admin.SetGmAsync(id, body.IsGm, token)));

        gm.MapGet("/accounts/{id:int}/characters", async (int id, AdminService admin, CancellationToken token) =>
            Results.Ok(await admin.CharactersAsync(id, token)));

        gm.MapDelete("/characters/{guid}", async (string guid, AdminService admin, CancellationToken token) =>
            Answer(await admin.DeleteCharacterAsync(guid, token)));

        gm.MapGet("/characters/{guid}", async (string guid, AdminService admin, CancellationToken token) =>
            await admin.CharacterAsync(guid, token) is { } found
                ? Results.Ok(new { found.Detail.Character, found.Detail.Gold, found.Detail.BagSlots, found.Detail.Bag, found.Online })
                : Results.Json(new { error = "No such character." }, statusCode: 404));

        gm.MapPatch("/characters/{guid}", async (string guid, CharacterEditRequest body, AdminService admin, CancellationToken token) =>
            Answer(await admin.EditCharacterAsync(guid, new CharacterEdit(body.Name, body.Level, body.Gold), token)));

        gm.MapPost("/characters/{guid}/items", async (string guid, GiveItemRequest body, AdminService admin, CancellationToken token) =>
            Answer(await admin.GiveItemAsync(guid, body.Entry, body.Count, token)));

        gm.MapGet("/items", (string? search, AdminService admin) => Results.Ok(admin.SearchItems(search ?? "")));

        gm.MapGet("/settings", (AdminService admin) => Results.Ok(admin.Settings().Select(view => new
        {
            name = view.Setting.Name,
            label = view.Setting.Label,
            kind = view.Setting.Kind.ToString(),
            help = view.Setting.Help,
            @default = view.Setting.Default,
            choices = view.Setting.Choices,
            needsRecreate = view.Setting.NeedsRecreate,
            value = view.Value,
        })));

        gm.MapGet("/settings/location", (AdminService admin) => Results.Ok(new { file = admin.SettingsLocation }));

        gm.MapPut("/settings", (SettingsRequest body, AdminService admin) =>
        {
            var result = admin.SaveSettings(body.Values);
            return result.Saved ? Results.Ok(new { saved = true }) : Results.Json(new { saved = false, problems = result.Problems }, statusCode: 400);
        });
    }

    private static IResult Answer(CharacterChange change) => change switch
    {
        CharacterChange.Done => Results.Ok(new { ok = true }),
        CharacterChange.NotFound => Results.Json(new { error = "No such character." }, statusCode: 404),
        CharacterChange.Online => Results.Json(new { error = "That character is in the world now. Change it once its player has left." }, statusCode: 409),
        CharacterChange.BadName => Results.Json(new { error = "A name is 2-16 letters." }, statusCode: 400),
        CharacterChange.NameTaken => Results.Json(new { error = "Another character has that name." }, statusCode: 409),
        CharacterChange.BadLevel => Results.Json(new { error = "Choose a level from 1 to 60." }, statusCode: 400),
        CharacterChange.BadGold => Results.Json(new { error = "That is more gold than a purse holds." }, statusCode: 400),
        CharacterChange.NoSuchItem => Results.Json(new { error = "There is no such item in the imported game data." }, statusCode: 400),
        CharacterChange.BadCount => Results.Json(new { error = "Give between 1 and 1000." }, statusCode: 400),
        _ => Results.Json(new { error = "The bag has no room for that." }, statusCode: 409),
    };

    private static IResult Answer(AccountChange change) => change switch
    {
        AccountChange.Done => Results.Ok(new { ok = true }),
        AccountChange.NotFound => Results.Json(new { error = "No such account." }, statusCode: 404),
        AccountChange.Taken => Results.Json(new { error = "That user id is taken." }, statusCode: 409),
        AccountChange.BadUserId => Results.Json(new { error = "A user id is 3-32 letters, digits or _ . @ -" }, statusCode: 400),
        _ => Results.Json(new { error = "A password needs at least 8 characters." }, statusCode: 400),
    };
}
