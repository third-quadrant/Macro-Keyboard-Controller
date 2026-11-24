using Fireworm.Core.Actions;
using Fireworm.Core.Device;
using Fireworm.Core.Models;
using Fireworm.Core.Settings;

var failures = 0;
var checks = 0;

void Check(string name, bool condition, string? detail = null)
{
    checks++;
    if (condition)
    {
        Console.WriteLine($"PASS  {name}");
    }
    else
    {
        failures++;
        Console.WriteLine($"FAIL  {name}" + (detail is null ? "" : $"  ({detail})"));
    }
}

void Section(string name) => Console.WriteLine($"\n== {name} ==");

Section("Trigger catalog");
Check("KEY1 VK = F13 (0x7C)", TriggerCatalog.TriggerVk(TriggerId.Key1) == 0x7C);
Check("KEY6 VK = F18 (0x81)", TriggerCatalog.TriggerVk(TriggerId.Key6) == 0x81);
Check("KEY1 usage = 0x68", TriggerCatalog.TriggerUsage(TriggerId.Key1) == 0x68);
Check("KEY6 usage = 0x6D", TriggerCatalog.TriggerUsage(TriggerId.Key6) == 0x6D);
Check("KEY1 slot = 1", TriggerCatalog.KeySlot(TriggerId.Key1) == 1);
Check("KEY6 slot = 6", TriggerCatalog.KeySlot(TriggerId.Key6) == 6);
Check("Knob EA = left", TriggerCatalog.FromConsumerUsage(0xEA) == TriggerId.KnobLeft);
Check("Knob E9 = right", TriggerCatalog.FromConsumerUsage(0xE9) == TriggerId.KnobRight);
Check("Knob E2 = click", TriggerCatalog.FromConsumerUsage(0xE2) == TriggerId.KnobClick);
Check("setting key KEY3", TriggerCatalog.SettingKey(TriggerId.Key3) == "KEY3");

Section("Vendor packet builder");
var key1 = VendorPacketBuilder.BuildKeyPackets(1, 1, 0x68, 0);
Check("key packet count = 3 (two reports + commit)", key1.Count == 3, $"count={key1.Count}");
Check("key slot = 1", key1[0][0] == 1);
Check("key type/layer = 0x11", key1[0][1] == 0x11, $"0x{key1[0][1]:X2}");
Check("group count = 1", key1[0][2] == 1);
Check("first sub-report index = 0", key1[0][3] == 0);
Check("second sub-report index = 1", key1[1][3] == 1);
Check("F13 usage in second report = 0x68", key1[1][5] == 0x68, $"0x{key1[1][5]:X2}");
Check("commit = AA AA", key1[2][0] == 0xAA && key1[2][1] == 0xAA);
var key6 = VendorPacketBuilder.BuildKeyPackets(6, 1, 0x6D, 0);
Check("KEY6 slot = 6", key6[0][0] == 6);
Check("F18 usage = 0x6D", key6[1][5] == 0x6D);
Check("layer switch = A1 01", VendorPacketBuilder.BuildLayerSwitch(1)[0] == 0xA1 && VendorPacketBuilder.BuildLayerSwitch(1)[1] == 1);
Check("layer 2 nibble = 0x21", VendorPacketBuilder.BuildKeyPackets(1, 2, 0x69, 0)[0][1] == 0x21);
var led = VendorPacketBuilder.BuildLedPackets(1, 1);
Check("LED slot = B0", led[0][0] == 0xB0);
Check("LED type/layer = 0x18", led[0][1] == 0x18);
Check("LED commit = AA A1", led[1][0] == 0xAA && led[1][1] == 0xA1);

Section("Key parser");
Check("C parses", KeyParser.TryParse("C", out var c, out _) && c.Key == 0x43 && c.Modifiers.Count == 0);
Check("Ctrl+Z parses", KeyParser.TryParse("Ctrl+Z", out var cz, out _) && cz.Key == 0x5A && cz.Modifiers.SequenceEqual(new ushort[] { 0x11 }));
Check("Ctrl+Shift+S parses", KeyParser.TryParse("Ctrl+Shift+S", out var css, out _) && css.Key == 0x53 && css.Modifiers.Count == 2);
Check("F11 parses", KeyParser.TryParse("F11", out var f11, out _) && f11.Key == 0x7A);
Check("F13 parses", KeyParser.TryParse("F13", out var f13, out _) && f13.Key == 0x7C);
Check("empty fails", !KeyParser.TryParse("", out _, out _));
Check("modifier-only fails", !KeyParser.TryParse("Ctrl+Shift", out _, out _));
Check("VKey A -> HID 0x04", KeyParser.TryVKeyToHidUsage(0x41, out var aUsage) && aUsage == 0x04);
Check("VKey C -> HID 0x06", KeyParser.TryVKeyToHidUsage(0x43, out var cUsage) && cUsage == 0x06);
Check("VKey F13 -> HID 0x68", KeyParser.TryVKeyToHidUsage(0x7C, out var f13Usage) && f13Usage == 0x68);

Section("Action engine (no input sent)");
var engine = new ActionEngine();
Check("none is success", engine.Execute(new ActionDefinition { Type = "none" }).Success);
Check("empty keyboard fails", !engine.Execute(new ActionDefinition { Type = "keyboard" }).Success);
Check("empty launch fails", !engine.Execute(new ActionDefinition { Type = "launch" }).Success);
Check("bad media fails", !engine.Execute(new ActionDefinition { Type = "media", Media = "nope" }).Success);

Section("Settings store");
var temp = Path.Combine(Path.GetTempPath(), "fireworm-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temp);
try
{
    var store = new SettingsStore(temp);
    var settings = store.Load();
    Check("default has 9 bindings", settings.Bindings.Count == 9, $"count={settings.Bindings.Count}");
    settings.SetAction(TriggerId.Key1, new ActionDefinition { Type = "keyboard", Keys = new List<string> { "C" } });
    store.Save(settings);
    var reloaded = store.Load();
    Check("round-trip KEY1 type", reloaded.GetAction(TriggerId.Key1).Type == "keyboard");
    Check("round-trip KEY1 keys", reloaded.GetAction(TriggerId.Key1).Keys.SequenceEqual(new[] { "C" }));

    File.WriteAllText(store.SettingsPath, "{ this is not json");
    var recovered = store.Load();
    Check("corrupt JSON recovers to defaults", recovered.Bindings.Count == 9);
    Check("corrupt JSON reports warning", !string.IsNullOrWhiteSpace(store.LastWarning));
    Check("corrupt file quarantined", Directory.GetFiles(temp, "settings.json.bad-*").Length == 1);
}
finally
{
    try { Directory.Delete(temp, true); } catch { }
}

Section("Device backup");
var backup = DeviceBackup.CreateObservedDefault();
Check("backup has 6 keys", backup.Keys.Count == 6);
Check("backup KEY1 = A (0x04)", backup.ForSlot(1)?.Usage == 0x04);
Check("backup KEY2 = C (0x06)", backup.ForSlot(2)?.Usage == 0x06);
Check("backup KEY6 = C (0x06)", backup.ForSlot(6)?.Usage == 0x06);
var restorePackets = VendorPacketBuilder.BuildRestorePackets(backup);
Check("restore builds packets for 6 keys", restorePackets.Count == 18, $"count={restorePackets.Count}");
Check("restore first packet slot=1 type=0x11", restorePackets[0][0] == 1 && restorePackets[0][1] == 0x11);

Section("LED feedback (no device)");
using (var feedback = new KeyLedFeedback(() => null, _ => { }))
{
    var threw = false;
    try
    {
        feedback.Pulse(1);
        feedback.Pulse(9); // out of range: ignored
    }
    catch
    {
        threw = true;
    }
    Check("LED pulse without device neither throws nor writes", !threw);
}

Section("Summary formatting");
Check("keyboard summary", new ActionDefinition { Type = "keyboard", Keys = new List<string> { "Ctrl+Z" } }.Summary().Contains("Ctrl+Z"));
Check("launch summary", new ActionDefinition { Type = "launch", Path = @"C:\Tools\app.exe" }.Summary().Contains("app.exe"));
Check("none summary", new ActionDefinition().Summary() == "설정 안 됨");

Console.WriteLine($"\n{checks - failures}/{checks} checks passed.");
return failures == 0 ? 0 : 1;
