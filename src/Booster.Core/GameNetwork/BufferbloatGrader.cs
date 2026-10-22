namespace Booster.Core.GameNetwork;

public enum BufferbloatGrade
{
    APlus,
    A,
    B,
    C,
    D,
    F,
}

public static class BufferbloatGrader
{
    public static BufferbloatGrade? Grade(double increaseMs, IReadOnlyList<double> boundsMs)
    {
        ArgumentNullException.ThrowIfNull(boundsMs);
        if (boundsMs.Count == 0)
            return null;

        for (var i = 0; i < boundsMs.Count; i++)
        {
            if (increaseMs < boundsMs[i])
                return (BufferbloatGrade)i;
        }
        return BufferbloatGrade.F;
    }
}
