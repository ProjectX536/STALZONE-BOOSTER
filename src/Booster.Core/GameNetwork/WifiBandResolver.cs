using Booster.Core.Abstractions;

namespace Booster.Core.GameNetwork;

public enum WifiBand { Band24GHz, Band5GHz, Band6GHz }

public static class WifiBandResolver
{
    private const string Source = "Wi-Fi channel number";

    public static Reading<WifiBand> FromChannel(int channel)
    {
        var matches = new List<WifiBand>(3);
        if (channel is >= 1 and <= 14)
            matches.Add(WifiBand.Band24GHz);
        if (IsChannel5GHz(channel))
            matches.Add(WifiBand.Band5GHz);
        if (IsChannel6GHz(channel))
            matches.Add(WifiBand.Band6GHz);

        return matches.Count switch
        {
            1 => Reading<WifiBand>.Known(matches[0], Source),
            0 => Reading<WifiBand>.Unknown(Source, "unknown channel"),
            _ => Reading<WifiBand>.Unknown(Source, "channel is ambiguous between bands"),
        };
    }

    private static bool IsChannel5GHz(int c) =>
        c is >= 32 and <= 144 && c % 2 == 0 || c is >= 149 and <= 177 && (c - 149) % 4 == 0;

    private static bool IsChannel6GHz(int c) => c == 2 || c is >= 1 and <= 233 && (c - 1) % 4 == 0;
}
