using Booster.Core.Inventory;

namespace Booster.Core.Abstractions;

/// <summary>Правило анализа снимка системы (docs/ARCHITECTURE.md, раздел 4). Реализации — с Этапа 2.</summary>
public interface IOptimizationRule
{
    string Id { get; }

    Task<RuleEvaluation> EvaluateAsync(SystemSnapshot snapshot, CancellationToken cancellationToken);
}

public sealed record RuleEvaluation(
    string RuleId,
    bool IsApplicable,
    IReadOnlyList<string> Evidence,
    IReadOnlyList<string> ProposedActionIds);
