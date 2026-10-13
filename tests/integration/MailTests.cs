using Ubiquitous.Common;
using Ubiquitous.Data;
using Ubiquitous.Persistence;
using Ubiquitous.Protocol.Web;
using Ubiquitous.Server;

namespace Ubiquitous.Integration.Tests;

public sealed class MailTests
{
    [Fact]
    public async Task FileMailStore_RoundTripsAPersistedMessage()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"ubiquitous-mail-{Guid.NewGuid():N}");
        try
        {
            var store = new FileMailStore(directory);
            var message = new PersistedMailMessage("mail-1", "char-b", "Ayla", "Hi", "body text", 25, 1000, false, false);
            await store.SaveAsync("char-b", [message]);

            var messages = await store.ListAsync("char-b");
            var loaded = Assert.Single(messages);
            Assert.Equal("Ayla", loaded.SenderName);
            Assert.Equal(25, loaded.Gold);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task MailSentWhileRecipientIsOffline_SurvivesARestartAndPaysOutOnClaim()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"ubiquitous-mail-session-{Guid.NewGuid():N}");
        try
        {
            var mailStore = new FileMailStore(directory);
            var characters = new InMemoryCharacterStore(
            [
                Character("char-a", 1, "Ayla"),
                Character("char-b", 2, "Borin"),
            ]);

            var sender = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
                characters, mailStore: mailStore);
            await sender.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
                WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"))));
            await sender.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
                WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"))));

            var sendResponses = await sender.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
                WebMessageTypes.MailSend, 2, new MailSendPayload("Borin", "Hello", "Safe travels!", 40))));
            Assert.Contains(sendResponses, r => r.Contains("\"type\":\"mail.sent\"") && r.Contains("\"ok\":true"));
            Assert.Contains(sendResponses, r => r.Contains("\"type\":\"player.updated\""));

            var mailboxSpawn = new[] { new StaticObjectSpawnDefinition(1, 13, 9102, 2, 2, 4, 0) };

            var recipientSessionA = TestSessions.Create(new DevelopmentAccountStore("developer2", "dev-secret", 2),
                characters, staticObjectSpawns: mailboxSpawn, mailStore: mailStore);
            await recipientSessionA.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
                WebMessageTypes.AccountLogin, 0, new LoginRequest("developer2", "dev-secret"))));

            var entered = await recipientSessionA.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
                WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Borin"))));
            Assert.DoesNotContain(entered, r => r.Contains("protocol.error"));

            var opened = await recipientSessionA.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
                WebMessageTypes.MailOpen, 2, new { networkId = "StaticObject:1" })));
            Assert.Contains(opened, r => r.Contains("\"type\":\"mail.opened\"") && r.Contains("\"open\":true"));
            var inboxReply = Assert.Single(opened, r => r.Contains("\"type\":\"mail.inbox\""));
            Assert.Contains("\"senderName\":\"Ayla\"", inboxReply);
            Assert.Contains("\"gold\":40", inboxReply);
            Assert.Contains("\"goldClaimed\":false", inboxReply);

            var mailId = ExtractMailId(inboxReply);

            var read = await recipientSessionA.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
                WebMessageTypes.MailRead, 3, new MailIdPayload(mailId))));
            var messageReply = Assert.Single(read);
            Assert.Contains("Safe travels!", messageReply);

            var claimed = await recipientSessionA.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
                WebMessageTypes.MailClaim, 4, new MailIdPayload(mailId))));
            Assert.Contains(claimed, r => r.Contains("\"type\":\"mail.claimed\"") && r.Contains("\"ok\":true") && r.Contains("\"gold\":40"));
            Assert.Contains(claimed, r => r.Contains("\"gold\":5040"));

            var recipientSessionB = TestSessions.Create(new DevelopmentAccountStore("developer2", "dev-secret", 2),
                characters, staticObjectSpawns: mailboxSpawn, mailStore: mailStore);
            await recipientSessionB.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
                WebMessageTypes.AccountLogin, 0, new LoginRequest("developer2", "dev-secret"))));
            await recipientSessionB.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
                WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Borin"))));

            var reopened = await recipientSessionB.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
                WebMessageTypes.MailOpen, 2, new { networkId = "StaticObject:1" })));
            var reopenedInbox = Assert.Single(reopened, r => r.Contains("\"type\":\"mail.inbox\""));
            Assert.Contains("\"goldClaimed\":true", reopenedInbox);
            Assert.Contains("\"read\":true", reopenedInbox);

            var secondClaim = await recipientSessionB.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
                WebMessageTypes.MailClaim, 3, new MailIdPayload(mailId))));
            Assert.Contains(secondClaim, r => r.Contains("\"ok\":false"));

            await recipientSessionB.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
                WebMessageTypes.MailDelete, 4, new MailIdPayload(mailId))));

            var recipientSessionC = TestSessions.Create(new DevelopmentAccountStore("developer2", "dev-secret", 2),
                characters, staticObjectSpawns: mailboxSpawn, mailStore: mailStore);
            await recipientSessionC.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
                WebMessageTypes.AccountLogin, 0, new LoginRequest("developer2", "dev-secret"))));
            await recipientSessionC.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
                WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Borin"))));
            var finalOpen = await recipientSessionC.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
                WebMessageTypes.MailOpen, 2, new { networkId = "StaticObject:1" })));
            var finalInbox = Assert.Single(finalOpen, r => r.Contains("\"type\":\"mail.inbox\""));
            Assert.Contains("\"messages\":[]", finalInbox);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task MailOpen_RefusesWithoutAReachableMailbox()
    {
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Character("char-a", 1, "Ayla")]));
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"))));
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"))));

        var opened = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.MailOpen, 2, new { networkId = "StaticObject:999" })));

        var reply = Assert.Single(opened);
        Assert.Contains("\"type\":\"mail.opened\"", reply);
        Assert.Contains("\"open\":false", reply);
    }

    [Fact]
    public async Task MailSend_ToUnknownPlayer_IsRefused()
    {
        var session = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
            new InMemoryCharacterStore([Character("char-a", 1, "Ayla")]));
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"))));
        await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"))));

        var sent = await session.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
            WebMessageTypes.MailSend, 2, new MailSendPayload("NoSuchPlayer", "Hi", "body", 0))));

        var reply = Assert.Single(sent);
        Assert.Contains("\"ok\":false", reply);
    }

    private static string ExtractMailId(string inboxJson)
    {
        const string marker = "\"id\":\"";
        var start = inboxJson.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
        var end = inboxJson.IndexOf('"', start);
        return inboxJson[start..end];
    }

    [Fact]
    public async Task MailAttachments_SurviveARestart_AndClaimIntoTheBag()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"ubiquitous-mail-items-{Guid.NewGuid():N}");
        try
        {
            var mailStore = new FileMailStore(directory);
            var characters = new InMemoryCharacterStore(
            [
                Character("char-a", 1, "Ayla"),
                Character("char-b", 2, "Borin"),
            ]);
            var mailboxSpawn = new[] { new StaticObjectSpawnDefinition(1, 13, 9102, 2, 2, 4, 0) };

            var sender = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
                characters, mailStore: mailStore);
            await sender.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
                WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"))));
            await sender.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
                WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"))));

            var bogusSend = await sender.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
                WebMessageTypes.MailSend, 2, new MailSendPayload("Borin", "Hello", "A gift", 10, ["no-such-item"]))));
            Assert.Contains(bogusSend, r => r.Contains("\"type\":\"mail.sent\"") && r.Contains("\"ok\":false"));

            var sendResponses = await sender.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
                WebMessageTypes.MailSend, 3, new MailSendPayload("Borin", "Hello", "A gift", 10, ["starter-item"]))));
            Assert.Contains(sendResponses, r => r.Contains("\"type\":\"mail.sent\"") && r.Contains("\"ok\":true"));
            Assert.Contains(sendResponses, r => r.Contains("\"type\":\"inventory.itemRemoved\"") && r.Contains("starter-item"));

            var resend = await sender.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
                WebMessageTypes.MailSend, 4, new MailSendPayload("Borin", "Hello", "Again", 0, ["starter-item"]))));
            Assert.Contains(resend, r => r.Contains("\"type\":\"mail.sent\"") && r.Contains("\"ok\":false"));

            var recipient = TestSessions.Create(new DevelopmentAccountStore("developer2", "dev-secret", 2),
                characters, staticObjectSpawns: mailboxSpawn, mailStore: mailStore);
            await recipient.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
                WebMessageTypes.AccountLogin, 0, new LoginRequest("developer2", "dev-secret"))));
            await recipient.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
                WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Borin"))));

            var opened = await recipient.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
                WebMessageTypes.MailOpen, 2, new { networkId = "StaticObject:1" })));
            var inboxReply = Assert.Single(opened, r => r.Contains("\"type\":\"mail.inbox\""));
            Assert.Contains("\"itemCount\":1", inboxReply);
            Assert.Contains("\"itemsClaimed\":false", inboxReply);
            var mailId = ExtractMailId(inboxReply);

            var read = await recipient.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
                WebMessageTypes.MailRead, 3, new MailIdPayload(mailId))));
            var messageReply = Assert.Single(read);
            Assert.Contains("A gift", messageReply);
            Assert.Contains("\"entry\":100", messageReply);

            var claimed = await recipient.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
                WebMessageTypes.MailClaim, 4, new MailIdPayload(mailId))));
            Assert.Contains(claimed, r => r.Contains("\"type\":\"mail.claimed\"") && r.Contains("\"ok\":true")
                && r.Contains("\"gold\":10") && r.Contains("\"entry\":100"));
            Assert.Contains(claimed, r => r.Contains("\"type\":\"inventory.itemAdded\"") && r.Contains("\"entry\":100"));
            Assert.Contains(claimed, r => r.Contains("\"gold\":5010"));

            var afterRestart = TestSessions.Create(new DevelopmentAccountStore("developer2", "dev-secret", 2),
                characters, staticObjectSpawns: mailboxSpawn, mailStore: mailStore);
            await afterRestart.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
                WebMessageTypes.AccountLogin, 0, new LoginRequest("developer2", "dev-secret"))));
            await afterRestart.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
                WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Borin"))));

            var reopened = await afterRestart.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
                WebMessageTypes.MailOpen, 2, new { networkId = "StaticObject:1" })));
            var reopenedInbox = Assert.Single(reopened, r => r.Contains("\"type\":\"mail.inbox\""));
            Assert.Contains("\"itemsClaimed\":true", reopenedInbox);
            Assert.Contains("\"goldClaimed\":true", reopenedInbox);

            var secondClaim = await afterRestart.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
                WebMessageTypes.MailClaim, 3, new MailIdPayload(mailId))));
            Assert.Contains(secondClaim, r => r.Contains("\"ok\":false"));
            Assert.DoesNotContain(secondClaim, r => r.Contains("\"type\":\"inventory.itemAdded\""));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task MailClaim_WithAFullBag_RefusesEverything_AndLosesNothing()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"ubiquitous-mail-fullbag-{Guid.NewGuid():N}");
        try
        {
            var mailStore = new FileMailStore(directory);
            var characters = new InMemoryCharacterStore(
            [
                Character("char-a", 1, "Ayla"),
                Character("char-b", 2, "Borin"),
            ]);
            var mailboxSpawn = new[] { new StaticObjectSpawnDefinition(1, 13, 9102, 2, 2, 4, 0) };

            var sender = TestSessions.Create(new DevelopmentAccountStore("developer", "dev-secret"),
                characters, mailStore: mailStore);
            await sender.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
                WebMessageTypes.AccountLogin, 0, new LoginRequest("developer", "dev-secret"))));
            await sender.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
                WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Ayla"))));
            var sendResponses = await sender.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
                WebMessageTypes.MailSend, 2, new MailSendPayload("Borin", "Hello", "A gift", 10, ["starter-item"]))));
            Assert.Contains(sendResponses, r => r.Contains("\"type\":\"mail.sent\"") && r.Contains("\"ok\":true"));

            var fullBag = new FixedInventoryStore(Enumerable.Range(0, 12)
                .Select(slot => new PersistedInventoryItem($"filler-{slot}", 900 + slot, 1, "bag", slot)).ToArray());
            var fullSession = TestSessions.Create(new DevelopmentAccountStore("developer2", "dev-secret", 2),
                characters, staticObjectSpawns: mailboxSpawn, mailStore: mailStore, inventoryStore: fullBag);
            await fullSession.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
                WebMessageTypes.AccountLogin, 0, new LoginRequest("developer2", "dev-secret"))));
            await fullSession.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
                WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Borin"))));

            var opened = await fullSession.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
                WebMessageTypes.MailOpen, 2, new { networkId = "StaticObject:1" })));
            var mailId = ExtractMailId(Assert.Single(opened, r => r.Contains("\"type\":\"mail.inbox\"")));

            var refused = await fullSession.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
                WebMessageTypes.MailClaim, 3, new MailIdPayload(mailId))));
            Assert.Contains(refused, r => r.Contains("\"type\":\"mail.claimed\"") && r.Contains("\"ok\":false"));
            Assert.DoesNotContain(refused, r => r.Contains("\"type\":\"inventory.itemAdded\""));
            Assert.DoesNotContain(refused, r => r.Contains("\"type\":\"player.updated\""));

            var reopened = await fullSession.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
                WebMessageTypes.MailOpen, 4, new { networkId = "StaticObject:1" })));
            var reopenedInbox = Assert.Single(reopened, r => r.Contains("\"type\":\"mail.inbox\""));
            Assert.Contains("\"itemsClaimed\":false", reopenedInbox);
            Assert.Contains("\"goldClaimed\":false", reopenedInbox);

            var roomySession = TestSessions.Create(new DevelopmentAccountStore("developer2", "dev-secret", 2),
                characters, staticObjectSpawns: mailboxSpawn, mailStore: mailStore);
            await roomySession.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
                WebMessageTypes.AccountLogin, 0, new LoginRequest("developer2", "dev-secret"))));
            await roomySession.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
                WebMessageTypes.CharactersChoose, 1, new ChooseCharacterRequest("Borin"))));

            var claimed = await roomySession.HandleTextAsync(WebMessageJson.Serialize(WebMessageEnvelope.Create(
                WebMessageTypes.MailClaim, 2, new MailIdPayload(mailId))));
            Assert.Contains(claimed, r => r.Contains("\"type\":\"mail.claimed\"") && r.Contains("\"ok\":true")
                && r.Contains("\"gold\":10") && r.Contains("\"entry\":100"));
            Assert.Contains(claimed, r => r.Contains("\"type\":\"inventory.itemAdded\"") && r.Contains("\"entry\":100"));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    private sealed class FixedInventoryStore(IReadOnlyList<PersistedInventoryItem> items) : IInventoryStore
    {
        public Task<IReadOnlyList<PersistedInventoryItem>> ListAsync(string characterGuid, CancellationToken cancellationToken = default) =>
            Task.FromResult(items);

        public Task SaveAsync(string characterGuid, IReadOnlyList<PersistedInventoryItem> items, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private static CharacterRecord Character(string guid, int accountId, string name) => new(
        guid, accountId, name, 1, 2, 3, 4, 5, 6, 7, 8,
        13, 1, 2, 3, 4, [100, 200]);
}
