using System.Text;

using Ubiquitous.Data;

namespace Ubiquitous.Game;

public enum CharacterCreateCode
{
    Ok = 0,

    NameNotAllowed = 1,

    NameTaken = 2,

    PairingNotPlayable = 3,

    LookNotAllowed = 4,

    NoRoom = 5,
}

public sealed record CharacterCreateRefusal(CharacterCreateCode Code, string Reason);

public sealed record NewCharacterPlan(
    string Name,
    int Race,
    int PlayerClass,
    int Gender,
    int DisplayInfoId,
    int Skin,
    int HairStyle,
    int HairColour,
    int Face,
    ushort MapId,
    float X,
    float Y,
    float Z,
    int Level,
    IReadOnlyList<int> Equipment,
    IReadOnlyList<StartingLoadoutItem> BagItems);

public static class CharacterCreation
{
    public const int MaxCharactersPerAccount = 4;

    public const int MinNameLength = 2;
    public const int MaxNameLength = 16;

    public const int StartingLevel = 1;

    public static string Tidy(string? name) => (name ?? string.Empty).Trim();

    public static bool IsNameAllowed(string tidied)
    {
        if (tidied.Length is < MinNameLength or > MaxNameLength) return false;
        foreach (var rune in tidied.EnumerateRunes())
        {
            if (!Rune.IsLetter(rune)) return false;
        }

        return true;
    }

    public static (NewCharacterPlan? Plan, CharacterCreateRefusal? Refusal) Plan(
        string? name,
        int race,
        int playerClass,
        int gender,
        int skin,
        int hairStyle,
        int hairColour,
        int face,
        int heldByAccount,
        bool nameIsTaken,
        IReadOnlySet<int>? offeredRaces = null)
    {
        if (heldByAccount >= MaxCharactersPerAccount)
        {
            return (null, new CharacterCreateRefusal(
                CharacterCreateCode.NoRoom,
                $"An account may hold {MaxCharactersPerAccount} characters."));
        }

        var tidied = Tidy(name);
        if (!IsNameAllowed(tidied))
        {
            return (null, new CharacterCreateRefusal(
                CharacterCreateCode.NameNotAllowed,
                $"A name is {MinNameLength} to {MaxNameLength} letters, and nothing but letters."));
        }

        if (nameIsTaken)
        {
            return (null, new CharacterCreateRefusal(
                CharacterCreateCode.NameTaken, "That name is taken."));
        }

        if (offeredRaces is not null && !offeredRaces.Contains(race))
        {
            return (null, new CharacterCreateRefusal(
                CharacterCreateCode.PairingNotPlayable, "That race is not open on this server."));
        }

        var loadout = StartingLoadouts.For(race, playerClass);
        if (loadout is null)
        {
            return (null, new CharacterCreateRefusal(
                CharacterCreateCode.PairingNotPlayable, "That race and class cannot be played together."));
        }

        if (PlayerBodies.DisplayInfoFor(race, gender) is not { } displayInfoId)
        {
            return (null, new CharacterCreateRefusal(
                CharacterCreateCode.LookNotAllowed, "That race has no such body."));
        }

        var display = new PlayerDisplayDefinition(
            gender, race, playerClass, displayInfoId, skin, hairStyle, hairColour, face);
        if (PlayerDisplayValidator.Validate(display).Count > 0)
        {
            return (null, new CharacterCreateRefusal(
                CharacterCreateCode.LookNotAllowed, "That is not a look a character can be created with."));
        }

        return (new NewCharacterPlan(
            tidied,
            race,
            playerClass,
            gender,
            displayInfoId,
            skin,
            hairStyle,
            hairColour,
            face,
            loadout.MapId,
            loadout.X,
            loadout.Y,
            loadout.Z,
            StartingLevel,
            loadout.WornEquipment(),
            loadout.BagItems), null);
    }
}
