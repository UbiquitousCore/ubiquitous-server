using Ubiquitous.Protocol.Web;

namespace Ubiquitous.Server;

public sealed record PlayerPresence(
    WebNetworkId NetworkId,
    ushort MapId,
    Position Position,
    float Yaw,
    string Name,
    int Level,
    int Class,
    int Race,
    int Gender,
    int DisplayInfoId,
    int Skin,
    int HairStyle,
    int HairColour,
    int Face,
    IReadOnlyList<int> Equipment,
    int Health,
    int MaxHealth,
    int Power,
    int MaxPower,
    IReadOnlyList<HeldAuraPayload>? Auras = null);
