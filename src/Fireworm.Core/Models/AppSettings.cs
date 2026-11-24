namespace Fireworm.Core.Models;

public sealed class DeviceSettings
{
    public int Vid { get; set; } = 0x1189;
    public int Pid { get; set; } = 0x8890;
}

public sealed class AppSettings
{
    public int Version { get; set; } = 1;
    public DeviceSettings Device { get; set; } = new();
    public Dictionary<string, ActionDefinition> Bindings { get; set; } = new();

    public static AppSettings CreateDefault()
    {
        var settings = new AppSettings();
        foreach (var trigger in TriggerCatalog.All)
            settings.Bindings[TriggerCatalog.SettingKey(trigger)] = new ActionDefinition();
        return settings;
    }

    public ActionDefinition GetAction(TriggerId trigger)
    {
        var key = TriggerCatalog.SettingKey(trigger);
        if (!Bindings.TryGetValue(key, out var action) || action == null)
        {
            action = new ActionDefinition();
            Bindings[key] = action;
        }
        return action;
    }

    public void SetAction(TriggerId trigger, ActionDefinition action)
        => Bindings[TriggerCatalog.SettingKey(trigger)] = action;

    public void EnsureAllBindings()
    {
        Bindings ??= new Dictionary<string, ActionDefinition>();
        foreach (var trigger in TriggerCatalog.All)
        {
            var key = TriggerCatalog.SettingKey(trigger);
            if (!Bindings.TryGetValue(key, out var action) || action == null)
                Bindings[key] = new ActionDefinition();
        }
    }
}
