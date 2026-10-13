namespace Ubiquitous.World;

public static class PlayerMovementLimits
{
    public const float ForwardSpeed = 6f;
    public const float MaxStepInForwardSpeeds = 3f;
    public const float MaxStepDistance = ForwardSpeed * MaxStepInForwardSpeeds;
}
