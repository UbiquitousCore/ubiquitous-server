namespace Ubiquitous.Game;

public sealed record WhisperEvent(string TargetId, string Text);

public sealed class ChatWhisperService
{
    public enum ValidateOutcome { Ok, MissingTarget, SelfWhisper, EmptyText }

    public sealed record ValidateResult(ValidateOutcome Outcome, WhisperEvent? Event);

    public ValidateResult Validate(string senderId, string? targetId, string? text)
    {
        if (string.IsNullOrWhiteSpace(targetId)) return new ValidateResult(ValidateOutcome.MissingTarget, null);
        if (string.Equals(senderId, targetId, StringComparison.Ordinal)) return new ValidateResult(ValidateOutcome.SelfWhisper, null);
        if (string.IsNullOrWhiteSpace(text)) return new ValidateResult(ValidateOutcome.EmptyText, null);

        var trimmed = text.Trim();
        if (trimmed.Length > 256) trimmed = trimmed[..256];
        return new ValidateResult(ValidateOutcome.Ok, new WhisperEvent(targetId, trimmed));
    }
}
