using Booster.Core.Engine;

namespace Booster.Core.Abstractions;

public interface IActionPolicy
{
    PolicyDecision Evaluate(ActionDescriptor action, ActionMode? configuredMode, PolicyContext context);
}
