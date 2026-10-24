using System.Text.RegularExpressions;
using Ubiquitous.Persistence;

namespace Ubiquitous.Server;

public static partial class AccountCommands
{
    public const int MinimumPasswordLength = 8;

    private const int ExitUsage = 2;
    private const int ExitRefused = 1;

    [GeneratedRegex("^[A-Za-z0-9_.@-]{3,32}$")]
    private static partial Regex UserIdPattern();

    public static async Task<int> RunAsync(
        string[] args, IAccountAdmin accounts, Func<string, string?> readPassword, TextWriter output, TextWriter error,
        int iterations = PasswordHasher.DefaultIterations)
    {
        switch (args)
        {
            case ["create", var userId]:
                return await CreateAsync(userId, accounts, readPassword, output, error, iterations);
            case ["password", var userId]:
                return await ChangePasswordAsync(userId, accounts, readPassword, output, error, iterations);
            case ["gm", var userId, "on" or "off"]:
                return await SetGameMasterAsync(userId, args[2] == "on", accounts, output, error);
            case ["list"]:
                foreach (var account in await accounts.ListAsync())
                    output.WriteLine($"{account.Id}\t{account.UserId}{(account.IsAdmin ? "\tGM" : "")}");
                return 0;
            default:
                error.WriteLine("usage: account create <userId> | account password <userId> | account gm <userId> on|off | account list");
                return ExitUsage;
        }
    }

    private static async Task<int> CreateAsync(
        string userId, IAccountAdmin accounts, Func<string, string?> readPassword, TextWriter output, TextWriter error, int iterations)
    {
        if (!UserIdPattern().IsMatch(userId))
        {
            error.WriteLine("The user id must be 3-32 letters, digits or _ . @ -");
            return ExitRefused;
        }

        if (await accounts.FindByUserIdAsync(userId) is not null)
        {
            error.WriteLine($"{userId} already exists");
            return ExitRefused;
        }

        if (AskForPassword(readPassword, error) is not { } password) return ExitRefused;
        var created = await accounts.CreateAsync(userId, PasswordHasher.Hash(password, iterations));
        if (created is null)
        {
            error.WriteLine($"{userId} already exists");
            return ExitRefused;
        }

        output.WriteLine($"created account {created.UserId} (id {created.Id})");
        return 0;
    }

    // Takes effect the next time that player signs in.
    private static async Task<int> SetGameMasterAsync(
        string userId, bool on, IAccountAdmin accounts, TextWriter output, TextWriter error)
    {
        if (await accounts.FindByUserIdAsync(userId) is not { } account)
        {
            error.WriteLine($"no account called {userId}");
            return ExitRefused;
        }

        await accounts.SetAdminAsync(account.Id, on);
        output.WriteLine($"{userId} is {(on ? "now" : "no longer")} a GM; it applies at their next sign-in");
        return 0;
    }

    private static async Task<int> ChangePasswordAsync(
        string userId, IAccountAdmin accounts, Func<string, string?> readPassword, TextWriter output, TextWriter error, int iterations)
    {
        if (await accounts.FindByUserIdAsync(userId) is not { } account)
        {
            error.WriteLine($"no account called {userId}");
            return ExitRefused;
        }

        if (AskForPassword(readPassword, error) is not { } password) return ExitRefused;
        await accounts.SetPassHashAsync(account.Id, PasswordHasher.Hash(password, iterations));
        output.WriteLine($"password changed for {userId}");
        return 0;
    }

    private static string? AskForPassword(Func<string, string?> readPassword, TextWriter error)
    {
        var password = readPassword("Password: ");
        if (string.IsNullOrEmpty(password) || password.Length < MinimumPasswordLength)
        {
            error.WriteLine($"The password must be at least {MinimumPasswordLength} characters");
            return null;
        }

        if (readPassword("Repeat password: ") != password)
        {
            error.WriteLine("The passwords did not match");
            return null;
        }

        return password;
    }
}
