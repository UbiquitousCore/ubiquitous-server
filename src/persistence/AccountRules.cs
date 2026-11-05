using System.Text.RegularExpressions;

namespace Ubiquitous.Persistence;

// What every way of making an account accepts: the command line and the admin tool alike.
public static partial class AccountRules
{
    public const int MinimumPasswordLength = 8;

    public const string UserIdHint = "The user id must be 3-32 letters, digits or _ . @ -";

    public static string PasswordHint => $"The password must be at least {MinimumPasswordLength} characters";

    [GeneratedRegex("^[A-Za-z0-9_.@-]{3,32}$")]
    private static partial Regex UserIdPattern();

    public static bool IsValidUserId(string userId) => UserIdPattern().IsMatch(userId);

    public static bool IsStrongEnough(string? password) =>
        !string.IsNullOrEmpty(password) && password.Length >= MinimumPasswordLength;
}
