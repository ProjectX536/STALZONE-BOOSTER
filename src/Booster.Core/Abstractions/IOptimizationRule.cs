using Booster.Core.Inventory;

namespace Booster.Core.Abstractions;

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
