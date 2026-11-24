namespace Fireworm.Core.Models;

/// <summary>Physical slot identity. Internal trigger keys are an implementation detail.</summary>
public enum TriggerId
{
    Key1,
    Key2,
    Key3,
    Key4,
    Key5,
    Key6,
    KnobLeft,
    KnobClick,
    KnobRight,
}

public static class TriggerCatalog
{
    public static readonly TriggerId[] Keys =
    {
        TriggerId.Key1, TriggerId.Key2, TriggerId.Key3,
        TriggerId.Key4, TriggerId.Key5, TriggerId.Key6,
    };

    public static readonly TriggerId[] Knobs =
    {
        TriggerId.KnobLeft, TriggerId.KnobClick, TriggerId.KnobRight,
    };

    public static readonly TriggerId[] All =
    {
        TriggerId.Key1, TriggerId.Key2, TriggerId.Key3,
        TriggerId.Key4, TriggerId.Key5, TriggerId.Key6,
        TriggerId.KnobLeft, TriggerId.KnobClick, TriggerId.KnobRight,
    };

    public static string SettingKey(TriggerId trigger) => trigger switch
    {
        TriggerId.Key1 => "KEY1",
        TriggerId.Key2 => "KEY2",
        TriggerId.Key3 => "KEY3",
        TriggerId.Key4 => "KEY4",
        TriggerId.Key5 => "KEY5",
        TriggerId.Key6 => "KEY6",
        TriggerId.KnobLeft => "KNOB_LEFT",
        TriggerId.KnobClick => "KNOB_CLICK",
        TriggerId.KnobRight => "KNOB_RIGHT",
        _ => throw new ArgumentOutOfRangeException(nameof(trigger)),
    };

    public static string DisplayName(TriggerId trigger) => trigger switch
    {
        TriggerId.Key1 => "KEY 1",
        TriggerId.Key2 => "KEY 2",
        TriggerId.Key3 => "KEY 3",
        TriggerId.Key4 => "KEY 4",
        TriggerId.Key5 => "KEY 5",
        TriggerId.Key6 => "KEY 6",
        TriggerId.KnobLeft => "노브 왼쪽",
        TriggerId.KnobClick => "노브 클릭",
        TriggerId.KnobRight => "노브 오른쪽",
        _ => trigger.ToString(),
    };

    /// <summary>Internal trigger; shown only in diagnostics, not in the normal mapping UI.</summary>
    public static string InternalTriggerLabel(TriggerId trigger) => trigger switch
    {
        TriggerId.Key1 => "F13",
        TriggerId.Key2 => "F14",
        TriggerId.Key3 => "F15",
        TriggerId.Key4 => "F16",
        TriggerId.Key5 => "F17",
        TriggerId.Key6 => "F18",
        TriggerId.KnobLeft => "Consumer 0x00EA",
        TriggerId.KnobClick => "Consumer 0x00E2",
        TriggerId.KnobRight => "Consumer 0x00E9",
        _ => "?",
    };

    public static bool IsKey(TriggerId trigger) => trigger is >= TriggerId.Key1 and <= TriggerId.Key6;

    /// <summary>Slot numbers are a working hypothesis from the vendor UI; verified by the apply wizard.</summary>
    public static int KeySlot(TriggerId trigger) => trigger switch
    {
        TriggerId.Key1 => 1,
        TriggerId.Key2 => 2,
        TriggerId.Key3 => 3,
        TriggerId.Key4 => 4,
        TriggerId.Key5 => 5,
        TriggerId.Key6 => 6,
        TriggerId.KnobLeft => 13,
        TriggerId.KnobClick => 14,
        TriggerId.KnobRight => 15,
        _ => 0,
    };

    /// <summary>HID keyboard usages used as internal triggers: F13..F18 = 0x68..0x6D.</summary>
    public static byte TriggerUsage(TriggerId trigger) => trigger switch
    {
        TriggerId.Key1 => 0x68,
        TriggerId.Key2 => 0x69,
        TriggerId.Key3 => 0x6A,
        TriggerId.Key4 => 0x6B,
        TriggerId.Key5 => 0x6C,
        TriggerId.Key6 => 0x6D,
        _ => 0,
    };

    /// <summary>Windows virtual-key codes delivered by Raw Input for F13..F18.</summary>
    public static int TriggerVk(TriggerId trigger) => trigger switch
    {
        TriggerId.Key1 => 0x7C,
        TriggerId.Key2 => 0x7D,
        TriggerId.Key3 => 0x7E,
        TriggerId.Key4 => 0x7F,
        TriggerId.Key5 => 0x80,
        TriggerId.Key6 => 0x81,
        _ => 0,
    };

    public static TriggerId? FromVk(int vk) => vk switch
    {
        0x7C => TriggerId.Key1,
        0x7D => TriggerId.Key2,
        0x7E => TriggerId.Key3,
        0x7F => TriggerId.Key4,
        0x80 => TriggerId.Key5,
        0x81 => TriggerId.Key6,
        _ => null,
    };

    public static TriggerId? FromConsumerUsage(int usage) => usage switch
    {
        0x00EA => TriggerId.KnobLeft,
        0x00E9 => TriggerId.KnobRight,
        0x00E2 => TriggerId.KnobClick,
        _ => null,
    };
}
