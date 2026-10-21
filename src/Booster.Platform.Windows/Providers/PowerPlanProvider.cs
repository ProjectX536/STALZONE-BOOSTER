using System.ComponentModel;
using Booster.Core.Abstractions;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Power;
using Windows.Win32.System.Registry;

namespace Booster.Platform.Windows.Providers;

public sealed unsafe class PowerPlanProvider : IPowerPlanProvider
{
    // SYSTEM_POWER_STATUS.BatteryFlag: батареи нет.
    private const byte NoSystemBattery = 128;

    public IReadOnlyList<PowerScheme> ListSchemes()
    {
        var schemes = new List<PowerScheme>();
        for (uint index = 0; ; index++)
        {
            Guid id;
            var size = (uint)sizeof(Guid);
            var error = PInvoke.PowerEnumerate(default(HKEY), (Guid*)null, (Guid*)null, POWER_DATA_ACCESSOR.ACCESS_SCHEME, index, (byte*)&id, &size);
            if (error == WIN32_ERROR.ERROR_NO_MORE_ITEMS)
                return schemes;
            Check(error);
            schemes.Add(new PowerScheme(id, PowerSchemeNames.Resolve(id, ReadFriendlyName)));
        }
    }

    public Guid GetActiveScheme()
    {
        Guid* active = null;
        Check(PInvoke.PowerGetActiveScheme(default(HKEY), &active));
        try
        {
            return *active;
        }
        finally
        {
            PInvoke.LocalFree((HLOCAL)active);
        }
    }

    public void SetActiveScheme(Guid schemeId)
    {
        if (!ListSchemes().Select(s => s.Id).Contains(schemeId))
            throw new ArgumentException($"Power scheme {schemeId} is not installed.", nameof(schemeId));
        Check(PInvoke.PowerSetActiveScheme(default(HKEY), &schemeId));
    }

    public PowerSource GetPowerSource()
    {
        if (!PInvoke.GetSystemPowerStatus(out var status))
            throw new Win32Exception();
        if ((status.BatteryFlag & NoSystemBattery) != 0)
            return PowerSource.AcPower;
        return status.ACLineStatus switch
        {
            1 => PowerSource.AcPower,
            0 => PowerSource.Battery,
            _ => PowerSource.Unknown,
        };
    }

    private static string ReadFriendlyName(Guid scheme)
    {
        uint size = 0;
        Check(PInvoke.PowerReadFriendlyName(default(HKEY), &scheme, null, null, null, &size));
        if (size == 0)
            return string.Empty;

        var buffer = new byte[size];
        fixed (byte* bytes = buffer)
        {
            Check(PInvoke.PowerReadFriendlyName(default(HKEY), &scheme, null, null, bytes, &size));
            return new string((char*)bytes).Trim();
        }
    }

    private static void Check(WIN32_ERROR error)
    {
        if (error != WIN32_ERROR.ERROR_SUCCESS)
            throw new Win32Exception((int)error);
    }
}

internal static class PowerSchemeNames
{
    public static string Resolve(Guid id, Func<Guid, string> read)
    {
        try
        {
            var name = read(id);
            return string.IsNullOrWhiteSpace(name) ? id.ToString() : name;
        }
        catch (Win32Exception)
        {
            return id.ToString();
        }
    }
}
