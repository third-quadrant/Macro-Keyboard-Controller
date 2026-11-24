using Fireworm.Core.Actions;
using Fireworm.Core.Input;
using Fireworm.Core.Models;

namespace Fireworm.Core.Actions;

/// <summary>
/// Separates physical input identification from action execution.
/// The resolver decides which action belongs to a trigger; the engine never sees the trigger.
/// </summary>
public sealed class TriggerRouter
{
    private readonly ActionEngine _engine;
    private readonly Func<TriggerId, ActionDefinition> _resolver;
    private readonly Action<string> _log;
    private readonly Dictionary<TriggerId, DateTime> _lastFire = new();
    private readonly object _sync = new();
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(150);

    public TriggerRouter(ActionEngine engine, Func<TriggerId, ActionDefinition> resolver, Action<string> log)
    {
        _engine = engine;
        _resolver = resolver;
        _log = log;
    }

    public void Handle(TriggerEvent trigger)
    {
        lock (_sync)
        {
            if (_lastFire.TryGetValue(trigger.Trigger, out var last) &&
                DateTime.UtcNow - last < Debounce)
                return;
            _lastFire[trigger.Trigger] = DateTime.UtcNow;
        }

        var action = _resolver(trigger.Trigger);
        if (action.IsNone)
        {
            _log($"{TriggerCatalog.DisplayName(trigger.Trigger)}: 설정된 동작 없음");
            return;
        }

        Task.Run(() =>
        {
            var result = _engine.Execute(action);
            _log($"{TriggerCatalog.DisplayName(trigger.Trigger)} → {result.Message}");
        });
    }
}
