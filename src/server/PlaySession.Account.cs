using System.Text.Json;
using Ubiquitous.Data;
using Ubiquitous.Game;
using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;
using DomainPosition = Ubiquitous.Common.Position;

namespace Ubiquitous.Server;

public sealed partial class PlaySession
{
    private IReadOnlyList<uint> KnownSpellsFor(CharacterRecord character)
    {
        var loadout = StartingLoadouts.For(character.Race, character.Class);
        if (loadout is null)
        {
            return _content.StartingSpells;
        }
        var books = GameContent.DevelopmentLoadout.Spells
            .Concat(PlaytestSpellbook.RangerToLevel40)
            .ToHashSet();
        _actionBar.Clear();
        foreach (var button in loadout.ActionBar) _actionBar[button.Slot] = (button.SpellId, 0);
        IReadOnlyList<uint> trained = character.Level >= LevelingService.MaxLevel
            ? PlaytestSpellbook.AtMaxLevel(character.Class)
            : [];
        return [.. _content.StartingSpells.Where(id => !books.Contains(id)), .. loadout.Spells, .. trained];
    }

    private async Task<string> LoginAsync(JsonElement payload, CancellationToken cancellationToken)
    {
        if (_loginAttempted)
        {
            return Error(WireCodes.ProtocolError.InvalidState, "Only one login attempt is allowed per connection.");
        }

        _loginAttempted = true;
        var request = WebMessageJson.DeserializePayload<LoginRequest>(payload);
        var result = await _accounts.AuthenticateAsync(request.UserId, request.PassHash, cancellationToken);

        if (result.Ok)
        {
            _account = await FindAccountAsync(request.UserId, cancellationToken);
        }

        return Reply(WebMessageTypes.AccountLoginResult, new ResultPayload(
            result.Ok, (int)result.Code, result.Reason));
    }

    private async Task<AccountRecord?> FindAccountAsync(string userId, CancellationToken cancellationToken)
    {
        if (_accounts is IAccountStore store)
        {
            return await store.FindByUserIdAsync(userId, cancellationToken);
        }

        return null;
    }

    private async Task<string> ListCharactersAsync(CancellationToken cancellationToken)
    {
        if (_account is null)
        {
            return Error(WireCodes.ProtocolError.NotAuthenticated, "Login is required before listing characters.");
        }

        var records = await _characters.ListForAccountAsync(_account.Id, cancellationToken);
        var summaries = records.Select(character => new CharacterSummary(
            character.Guid,
            character.Name,
            character.Level,
            character.Gender,
            character.Race,
            character.Class,
            character.DisplayInfoId,
            new Appearance(character.Skin, character.HairStyle, character.HairColour, character.Face),
            character.MapId,
            new Position(character.X, character.Y, character.Z),
            character.Equipment,
            LevelingService.HeldXp(character.Level, character.Xp),
            LevelingService.XpToNextLevel(character.Level))).ToArray();

        var offered = _content.PlayableRaces.OrderBy(race => race).ToArray();
        return Reply(WebMessageTypes.CharactersListed, new CharactersListedPayload(summaries, offered));
    }

    private async Task<IReadOnlyList<string>> CreateCharacterAsync(
        JsonElement payload,
        CancellationToken cancellationToken)
    {
        if (_account is null)
        {
            return [Error(WireCodes.ProtocolError.NotAuthenticated, "Login is required before making a character.")];
        }

        if (_character is not null)
        {
            return [Error(WireCodes.ProtocolError.InvalidState, "A character is already selected for this connection.")];
        }

        var request = WebMessageJson.DeserializePayload<CreateCharacterRequest>(payload);
        var look = request.Appearance ?? new Appearance(0, 0, 0, 0);
        var held = await _characters.ListForAccountAsync(_account.Id, cancellationToken);
        var tidied = CharacterCreation.Tidy(request.Name);
        var nameIsTaken = CharacterCreation.IsNameAllowed(tidied)
            && await _characters.FindByNameAsync(tidied, cancellationToken) is not null;

        var (plan, refusal) = CharacterCreation.Plan(
            request.Name, request.Race, request.Class, request.Gender,
            look.Skin, look.HairStyle, look.HairColour, look.Face,
            held.Count, nameIsTaken,
            _content.PlayableRaces);

        if (plan is null)
        {
            return [Reply(WebMessageTypes.CharactersCreated,
                new ResultPayload(false, (int)refusal!.Code, refusal.Reason))];
        }

        var record = new CharacterRecord(
            Guid.NewGuid().ToString("N"),
            _account.Id,
            plan.Name,
            plan.Gender,
            plan.Race,
            plan.PlayerClass,
            plan.DisplayInfoId,
            plan.Skin,
            plan.HairStyle,
            plan.HairColour,
            plan.Face,
            plan.MapId,
            plan.X,
            plan.Y,
            plan.Z,
            plan.Level,
            plan.Equipment);

        try
        {
            await _characters.CreateAsync(record, cancellationToken);
        }
        catch (CharacterNameTakenException)
        {
            return [Reply(WebMessageTypes.CharactersCreated,
                new ResultPayload(false, (int)CharacterCreateCode.NameTaken, "That name is taken."))];
        }

        await SeedCreatedBagAsync(record.Guid, plan.BagItems, cancellationToken);

        return
        [
            Reply(WebMessageTypes.CharactersCreated, new ResultPayload(true, (int)CharacterCreateCode.Ok)),
            await ListCharactersAsync(cancellationToken),
        ];
    }

    private async Task SeedCreatedBagAsync(
        string guid,
        IReadOnlyList<StartingLoadoutItem> bagItems,
        CancellationToken cancellationToken)
    {
        if (_inventoryStore is null || bagItems.Count == 0) return;
        var rows = bagItems
            .Select((item, index) => new Ubiquitous.Common.PersistedInventoryItem(
                $"created-{index}", (int)item.ItemEntry, item.Count, "bag", item.BagIndex))
            .ToArray();
        await _inventoryStore.SaveAsync(guid, rows, cancellationToken);
    }

    private string Outfit(JsonElement payload)
    {
        if (_account is null)
        {
            return Error(WireCodes.ProtocolError.NotAuthenticated, "Login is required before asking about a character.");
        }

        var request = WebMessageJson.DeserializePayload<OutfitRequest>(payload);
        var loadout = StartingLoadouts.For(request.Race, request.Class);
        return Reply(WebMessageTypes.CharactersOutfitted, new OutfitPayload(
            request.Race,
            request.Class,
            loadout?.WornEquipment() ?? []));
    }

    private async Task<IReadOnlyList<string>> DeleteCharacterAsync(
        JsonElement payload,
        CancellationToken cancellationToken)
    {
        if (_account is null)
        {
            return [Error(WireCodes.ProtocolError.NotAuthenticated, "Login is required before deleting a character.")];
        }

        if (_character is not null)
        {
            return [Error(WireCodes.ProtocolError.InvalidState, "A character cannot be deleted from inside the world.")];
        }

        var request = WebMessageJson.DeserializePayload<DeleteCharacterRequest>(payload);
        var held = await _characters.ListForAccountAsync(_account.Id, cancellationToken);
        var doomed = held.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, request.Name, StringComparison.OrdinalIgnoreCase)
            || string.Equals(candidate.Guid, request.Name, StringComparison.Ordinal));

        if (doomed is null)
        {
            return [Reply(WebMessageTypes.CharactersDeleted,
                new ResultPayload(false, (int)CharacterCreateCode.NameNotAllowed,
                    "This account holds no such character."))];
        }

        await _characters.DeleteAsync(doomed.Guid, cancellationToken);
        if (_inventoryStore is not null)
            await _inventoryStore.SaveAsync(doomed.Guid, [], cancellationToken);
        if (_questLogStore is not null)
            await _questLogStore.SaveAsync(doomed.Guid, Ubiquitous.Common.PersistedQuestLog.Empty, cancellationToken);

        return
        [
            Reply(WebMessageTypes.CharactersDeleted, new ResultPayload(true, (int)CharacterCreateCode.Ok)),
            await ListCharactersAsync(cancellationToken),
        ];
    }

    private async Task<IReadOnlyList<string>> ChooseCharacterAsync(
        JsonElement payload,
        CancellationToken cancellationToken)
    {
        if (_account is null)
        {
            return [Error(WireCodes.ProtocolError.NotAuthenticated, "Login is required before choosing a character.")];
        }

        if (_character is not null)
        {
            return [Error(WireCodes.ProtocolError.InvalidState, "A character is already selected for this connection.")];
        }

        var request = WebMessageJson.DeserializePayload<ChooseCharacterRequest>(payload);
        var characters = await _characters.ListForAccountAsync(_account.Id, cancellationToken);
        var character = characters.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, request.Name, StringComparison.Ordinal)
            || string.Equals(candidate.Guid, request.Name, StringComparison.Ordinal));

        if (character is null)
        {
            return [Reply(WebMessageTypes.CharactersChosen,
                new ResultPayload(false, (int)LoginCode.InvalidCredentials, "Character not found."))];
        }

        return await EnterAsCharacterAsync(character, cancellationToken);
    }

    public bool LeftForCharacterList { get; private set; }

    private string ReturnToCharacterList()
    {
        if (_character is null)
            return Error(WireCodes.ProtocolError.InvalidState, "There is no character in the world to leave.");
        LeftForCharacterList = true;
        return Reply(WebMessageTypes.CharactersReturned, new { });
    }

    public void CarryOnFrom(PlaySession left)
    {
        _account = left._account;
        _loginAttempted = left._loginAttempted;
        _outbox.ContinueFrom(left._outbox);
    }

    private async Task<IReadOnlyList<string>> EnterAsCharacterAsync(
        CharacterRecord character, CancellationToken cancellationToken)
    {
        if (!_online.TryClaim(character.Guid))
        {
            return [Reply(WebMessageTypes.CharactersChosen,
                new ResultPayload(false, (int)LoginCode.InvalidCredentials, "That character is already in the world."))];
        }

        _claimedCharacterGuid = character.Guid;
        Volatile.Write(ref _nextAutosaveTicks, (Now + AutosaveInterval).UtcTicks);
        _character = character;
        _level = character.Level;
        _xp = LevelingService.HeldXp(character.Level, character.Xp);
        _belongings.Currency = character.Currency ?? _content.StartingCurrency;
        if (_account is not null && _developmentUserIds.Contains(_account.UserId))
            _belongings.Currency = Math.Max(_belongings.Currency, DevelopmentFixtures.Purse);
        _belongings.BagSlots = character.BagSlots ?? PlayerBelongings.DefaultBagCapacity;
        _belongings.BankSlots = character.BankSlots ?? PlayerBelongings.DefaultBankCapacity;
        var startingAttributes = StartingLoadouts.For(character.Race, character.Class)?.Attributes;
        _combat.EnterAsCharacter(character.Class, character.Level, $"player:{character.Guid}", character.Health,
            startingAttributes);
        RefreshWornAttributes();
        _learnedSpells.Clear();
        _learnedSpells.AddRange(character.LearnedSpells ?? []);
        RestoreTalents(character, [.. KnownSpellsFor(character), .. _learnedSpells]);
        var knownSpells = SpellBookWithTalents();
        _spells.EnterAsCharacter(character.Class, character.Level, knownSpells, startingAttributes?.Intellect ?? 0);
        await LoadInventoryAsync(cancellationToken);
        _belongings.Items.ResettleWorn(InventoryTypeOf);
        _belongings.Items.SeedWorn(character.Equipment);
        character = character with { Equipment = _belongings.Items.WornEquipment() };
        _character = character;
        await LoadQuestLogAsync(cancellationToken);
        var responses = new List<string>(EnterWorld(character.MapId,
            new DomainPosition(character.X, character.Y, character.Z), 0f));
        responses.Add(Reply(WebMessageTypes.InventoryUpdated, InventorySnapshot()));
        responses.Add(Reply(WebMessageTypes.QuestLog, new QuestLogPayload(
            _quests.Log(_content.Quests).Select(QuestLogEntryPayloadFor).ToArray())));
        responses.Add(Reply(WebMessageTypes.SpellBook, new SpellBookPayload(
            [.. knownSpells], [.. _actionBar.Select(ActionSlotPayloadFor)])));
        responses.Add(TalentStateReply());
        var playerId = $"player:{character.Guid}";
        var enterNow = Now;
        var enterStaminaBonus = _attributeModifiers.GetFlatAmount(playerId, AttributeIds.Stamina, enterNow);
        var enterWisdomBonus = _attributeModifiers.GetFlatAmount(playerId, AttributeIds.Wisdom, enterNow);
        var enterValues = new Dictionary<string, int>
        {
            [WireCodes.UnitValue.Gold] = _belongings.Currency,
            [WireCodes.UnitValue.Level] = _level,
            [WireCodes.UnitValue.Health] = _combat.PlayerHealth,
            [WireCodes.UnitValue.MaxHealth] = _combat.EffectiveMaxHealth(enterStaminaBonus),
            [WireCodes.UnitValue.Power] = _spells.Power,
            [WireCodes.UnitValue.MaxPower] = _spells.EffectiveMaxPower(enterWisdomBonus),
            [WireCodes.UnitValue.Xp] = _xp,
            [WireCodes.UnitValue.XpToNextLevel] = LevelingService.XpToNextLevel(_level),
        };
        AddAttributesTo(enterValues, enterNow);
        responses.Add(Reply(WebMessageTypes.PlayerValuesChanged, new PlayerValuesChangedPayload(enterValues)));
        responses.AddRange(await _partyHandler.RejoinAsync());

        return responses;
    }

    private async Task<IReadOnlyList<string>> DevEnterWorldAsync(
        JsonElement payload, CancellationToken cancellationToken)
    {
        if (!_developmentEntry)
            return [Error(WireCodes.ProtocolError.Forbidden, "Development entry is not enabled on this server.")];
        if (_character is not null)
            return [Error(WireCodes.ProtocolError.InvalidState, "A character is already selected for this connection.")];
        if (!payload.TryGetProperty("characterRef", out var refValue) || refValue.ValueKind != JsonValueKind.String)
            return [Error(WireCodes.ProtocolError.InvalidState, "A character reference is required.")];

        var character = await _characters.GetByGuidAsync(refValue.GetString()!, cancellationToken);
        if (character is null)
            return [Error(WireCodes.ProtocolError.InvalidState, "No character matches that reference.")];

        _loginAttempted = true;
        _account = new AccountRecord(character.AccountId, $"dev:{character.Guid}", string.Empty);
        return await EnterAsCharacterAsync(character, cancellationToken);
    }
}
