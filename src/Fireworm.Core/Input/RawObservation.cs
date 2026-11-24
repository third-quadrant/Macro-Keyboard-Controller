namespace Fireworm.Core.Input;

public enum RawObservationKind
{
    Keyboard,
    Consumer,
    Mouse,
}

public sealed record RawObservation(
    string DevicePath,
    RawObservationKind Kind,
    int VKey,
    int Usage,
    string Description,
    DateTime Timestamp);
