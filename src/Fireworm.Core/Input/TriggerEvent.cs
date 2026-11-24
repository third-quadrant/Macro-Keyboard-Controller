using Fireworm.Core.Models;

namespace Fireworm.Core.Input;

public sealed record TriggerEvent(TriggerId Trigger, string DevicePath, string Description, DateTime Timestamp);
