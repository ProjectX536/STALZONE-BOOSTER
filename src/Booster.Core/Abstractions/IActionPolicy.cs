using Booster.Core.Engine;

namespace Booster.Core.Abstractions;

/// <summary>Единая точка решения, можно ли выполнить действие. Ни одно действие не выполняется в обход неё.</summary>
public interface IActionPolicy
{
    /// <param name="configuredMode">Режим действия из профиля; null — действие профилю неизвестно.</param>
    PolicyDecision Evaluate(ActionDescriptor action, ActionMode? configuredMode, PolicyContext context);
}
