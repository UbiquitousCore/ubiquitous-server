namespace Ubiquitous.Persistence;

public sealed class CharacterNameTakenException(string name)
    : Exception($"The character name '{name}' is already taken.");
