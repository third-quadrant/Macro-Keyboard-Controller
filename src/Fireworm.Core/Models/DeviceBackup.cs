namespace Fireworm.Core.Models;

/// <summary>
/// Behaviour-level backup of the six key slots. The device cannot be read back,
/// so this records what the keys emitted when the backup was taken.
/// </summary>
public sealed class KeyBackup
{
    public int Slot { get; set; }
    public string Key { get; set; } = "";
    public string Type { get; set; } = "keyboard"; // keyboard | media
    public byte Usage { get; set; }
    public byte Modifier { get; set; }
    public string Description { get; set; } = "";
}

public sealed class DeviceBackup
{
    public int Version { get; set; } = 1;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public List<KeyBackup> Keys { get; set; } = new();
    public string KnobNote { get; set; } = "Observed knob: 0xEA volume-, 0xE9 volume+, 0xE2 mute (not modified by this app).";
    public string Note { get; set; } = "Behaviour-level backup. Full device configuration read-back is unavailable.";

    /// <summary>Observed on the target unit before any write: KEY1=A, KEY2..KEY6=C, no modifiers.</summary>
    public static DeviceBackup CreateObservedDefault()
    {
        var backup = new DeviceBackup();
        backup.Keys.Add(new KeyBackup { Slot = 1, Key = "KEY1", Type = "keyboard", Usage = 0x04, Modifier = 0, Description = "A" });
        for (var slot = 2; slot <= 6; slot++)
            backup.Keys.Add(new KeyBackup { Slot = slot, Key = $"KEY{slot}", Type = "keyboard", Usage = 0x06, Modifier = 0, Description = "C" });
        return backup;
    }

    public KeyBackup? ForSlot(int slot) => Keys.FirstOrDefault(k => k.Slot == slot);
}
