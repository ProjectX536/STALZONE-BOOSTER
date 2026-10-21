using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Booster.Core.Abstractions;
using Booster.Core.Inventory;

namespace Booster.Providers.Gpu.Nvidia;

/// <summary>
/// nvapi64.dll, загруженный динамически и только из System32. Адреса функций запрашиваются через
/// <c>nvapi_QueryInterface</c> лениво и только по идентификаторам из <see cref="NvApiFunctionIds"/>; нулевой адрес —
/// NO_IMPLEMENTATION. Все вызовы — под одной блокировкой. DRS-сессия закрывается в <c>finally</c> без сохранения.
/// </summary>
internal sealed unsafe class NvApiLibrary : INvApi
{
    public const string LibraryName = "nvapi64.dll";
    public const string Source = "nvapi";
    private const string QueryInterfaceExport = "nvapi_QueryInterface";

    private static readonly IReadOnlyDictionary<uint, NvDrsValue> NoValues = new Dictionary<uint, NvDrsValue>();

    private readonly Lock _lock = new();
    private readonly Dictionary<uint, nint> _functions = [];
    private readonly nint _module;
    private readonly delegate* unmanaged[Cdecl]<uint, nint> _queryInterface;
    private bool _disposed;

    private NvApiLibrary(nint module, nint queryInterface)
    {
        _module = module;
        _queryInterface = (delegate* unmanaged[Cdecl]<uint, nint>)queryInterface;
    }

    public string DriverVersion { get; private set; } = string.Empty;

    /// <summary>Код для отсутствующей функции: после Dispose — API_NOT_INITIALIZED, иначе драйвер её не реализует.</summary>
    private int Missing => _disposed ? NvApiStatus.ApiNotInitialized : NvApiStatus.NoImplementation;

    /// <summary>Загружает библиотеку и вызывает NvAPI_Initialize; при неудаче библиотека выгружается.</summary>
    internal static NvApiLibrary? TryLoad(string libraryName, out int status)
    {
        if (!NativeLibrary.TryLoad(libraryName, typeof(NvApiLibrary).Assembly, DllImportSearchPath.System32, out var module))
        {
            status = NvApiStatus.LibraryNotFound;
            return null;
        }
        if (!NativeLibrary.TryGetExport(module, QueryInterfaceExport, out var queryInterface))
        {
            NativeLibrary.Free(module);
            status = NvApiStatus.LibraryNotFound;
            return null;
        }

        var library = new NvApiLibrary(module, queryInterface);
        var initialize = (delegate* unmanaged[Cdecl]<int>)library.Function(NvApiFunctionIds.Initialize);
        status = initialize == null ? NvApiStatus.NoImplementation : initialize();
        if (status != NvApiStatus.Ok)
        {
            NativeLibrary.Free(module);
            return null;
        }
        library.DriverVersion = library.ReadDriverVersion();
        return library;
    }

    /// <summary>NvAPI_SYS_GetDriverAndBranchVersion: 56094 → "560.94".</summary>
    internal static string FormatDriverVersion(uint version) =>
        string.Create(CultureInfo.InvariantCulture, $"{version / 100}.{version % 100:D2}");

    /// <summary>NvAPI_GPU_GetPCIIdentifiers: pDeviceId = (DEV &lt;&lt; 16) | VEN, pSubSystemId — как SUBSYS_ в Hardware ID.</summary>
    internal static PciId ToPciId(uint deviceId, uint subSystemId, uint revisionId) =>
        new((ushort)(deviceId & 0xFFFF), (ushort)(deviceId >> 16), subSystemId, (byte)(revisionId & 0xFF));

    public NvResult<IReadOnlyList<NvPhysicalGpu>> EnumeratePhysicalGpus()
    {
        lock (_lock)
        {
            var enumerate = (delegate* unmanaged[Cdecl]<NvPhysicalGpuHandles*, uint*, int>)Bound(NvApiFunctionIds.EnumPhysicalGpus);
            if (enumerate == null)
                return new(Missing, null);

            NvPhysicalGpuHandles handles;
            uint count;
            var status = enumerate(&handles, &count);
            if (status != NvApiStatus.Ok)
                return new(status, null);

            var gpus = new List<NvPhysicalGpu>();
            for (var i = 0; i < (int)Math.Min(count, NvPhysicalGpuHandles.MaxCount); i++)
            {
                var gpu = handles[i];
                gpus.Add(new NvPhysicalGpu(gpu, FullName(gpu), Luid(gpu), Pci(gpu)));
            }
            return new(NvApiStatus.Ok, gpus);
        }
    }

    public NvResult<uint?> GetGraphicsUtilizationPercent(nint gpu)
    {
        lock (_lock)
        {
            var get = (delegate* unmanaged[Cdecl]<nint, NvDynamicPstatesInfoEx*, int>)Bound(NvApiFunctionIds.GpuGetDynamicPstatesInfoEx);
            if (get == null)
                return new(Missing, null);

            var info = new NvDynamicPstatesInfoEx { Version = NvDynamicPstatesInfoEx.StructVersion };
            var status = get(gpu, &info);
            if (status != NvApiStatus.Ok)
                return new(status, null);
            var domain = info.Utilization[NvDynamicPstatesInfoEx.GpuDomain];
            return new(NvApiStatus.Ok, domain.IsPresent ? domain.Percentage : null);
        }
    }

    public NvResult<int?> GetGpuTemperatureC(nint gpu)
    {
        lock (_lock)
        {
            var get = (delegate* unmanaged[Cdecl]<nint, uint, NvThermalSettings*, int>)Bound(NvApiFunctionIds.GpuGetThermalSettings);
            if (get == null)
                return new(Missing, null);

            var settings = new NvThermalSettings { Version = NvThermalSettings.StructVersion };
            var status = get(gpu, NvThermalSettings.AllSensors, &settings);
            if (status != NvApiStatus.Ok)
                return new(status, null);
            for (var i = 0; i < (int)Math.Min(settings.Count, 3u); i++)
            {
                if (settings.Sensor[i].Target == NvThermalSettings.TargetGpu)
                    return new(NvApiStatus.Ok, settings.Sensor[i].CurrentTemp);
            }
            return new(NvApiStatus.Ok, null);
        }
    }

    public NvResult<(uint? GraphicsKhz, uint? MemoryKhz)> GetCurrentClocks(nint gpu)
    {
        lock (_lock)
        {
            var get = (delegate* unmanaged[Cdecl]<nint, NvClockFrequencies*, int>)Bound(NvApiFunctionIds.GpuGetAllClockFrequencies);
            if (get == null)
                return new(Missing, default);

            var clocks = new NvClockFrequencies
            {
                Version = NvClockFrequencies.StructVersion,
                ClockType = NvClockFrequencies.CurrentFrequency,
            };
            var status = get(gpu, &clocks);
            if (status != NvApiStatus.Ok)
                return new(status, default);
            var graphics = clocks.Domain[NvClockFrequencies.GraphicsDomain];
            var memory = clocks.Domain[NvClockFrequencies.MemoryDomain];
            return new(NvApiStatus.Ok, (graphics.IsPresent ? graphics.FrequencyKhz : null, memory.IsPresent ? memory.FrequencyKhz : null));
        }
    }

    public NvResult<uint> GetPerfDecreaseFlags(nint gpu) => ReadUInt32(gpu, NvApiFunctionIds.GpuGetPerfDecreaseInfo);

    public NvResult<uint> GetFanRpm(nint gpu) => ReadUInt32(gpu, NvApiFunctionIds.GpuGetTachReading);

    public NvResult<NvDrsRead> ReadDrs(string? application, IReadOnlyList<uint> settingIds)
    {
        ArgumentNullException.ThrowIfNull(settingIds);
        if (application is { Length: >= NvUnicodeString.Length })
            return new(NvApiStatus.InvalidArgument, null);

        lock (_lock)
        {
            var create = (delegate* unmanaged[Cdecl]<nint*, int>)Bound(NvApiFunctionIds.DrsCreateSession);
            var destroy = (delegate* unmanaged[Cdecl]<nint, int>)Bound(NvApiFunctionIds.DrsDestroySession);
            if (create == null || destroy == null)
                return new(Missing, null);

            nint session;
            var status = create(&session);
            if (status != NvApiStatus.Ok)
                return new(status, null);
            try
            {
                return ReadSession(session, application, settingIds);
            }
            finally
            {
                destroy(session);
            }
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
                return;
            var unload = (delegate* unmanaged[Cdecl]<int>)Function(NvApiFunctionIds.Unload);
            if (unload != null)
                unload();
            _disposed = true;
            _functions.Clear();
            NativeLibrary.Free(_module);
        }
    }

    private NvResult<NvDrsRead> ReadSession(nint session, string? application, IReadOnlyList<uint> settingIds)
    {
        var load = (delegate* unmanaged[Cdecl]<nint, int>)Function(NvApiFunctionIds.DrsLoadSettings);
        if (load == null)
            return new(NvApiStatus.NoImplementation, null);
        var status = load(session);
        if (status != NvApiStatus.Ok)
            return new(status, null);

        nint profile;
        if (application is null)
        {
            var global = (delegate* unmanaged[Cdecl]<nint, nint*, int>)Function(NvApiFunctionIds.DrsGetCurrentGlobalProfile);
            if (global == null)
                return new(NvApiStatus.NoImplementation, null);
            status = global(session, &profile);
        }
        else
        {
            var find = (delegate* unmanaged[Cdecl]<nint, NvUnicodeString*, nint*, NvDrsApplication*, int>)Function(
                NvApiFunctionIds.DrsFindApplicationByName);
            if (find == null)
                return new(NvApiStatus.NoImplementation, null);
            var name = default(NvUnicodeString);
            application.AsSpan().CopyTo(name);
            var found = new NvDrsApplication { Version = NvDrsApplication.StructVersion };
            status = find(session, &name, &profile, &found);
            if (status == NvApiStatus.ExecutableNotFound)
                return new(NvApiStatus.Ok, new NvDrsRead(null, NoValues));
        }
        if (status != NvApiStatus.Ok)
            return new(status, null);

        var getInfo = (delegate* unmanaged[Cdecl]<nint, nint, NvDrsProfile*, int>)Function(NvApiFunctionIds.DrsGetProfileInfo);
        var getSetting = (delegate* unmanaged[Cdecl]<nint, nint, uint, NvDrsSetting*, int>)Function(NvApiFunctionIds.DrsGetSetting);
        if (getInfo == null || getSetting == null)
            return new(NvApiStatus.NoImplementation, null);
        var description = new NvDrsProfile { Version = NvDrsProfile.StructVersion };
        status = getInfo(session, profile, &description);
        if (status != NvApiStatus.Ok)
            return new(status, null);

        var values = new Dictionary<uint, NvDrsValue>();
        NvDrsSetting setting;
        foreach (var id in settingIds)
        {
            setting = default;
            setting.Version = NvDrsSetting.StructVersion;
            status = getSetting(session, profile, id, &setting);
            if (status == NvApiStatus.SettingNotFound)
                continue;
            if (status != NvApiStatus.Ok)
                return new(status, null);
            // Строковые и двоичные параметры в первом NvU32 значения не хранят — такое число было бы выдуманным.
            if (setting.SettingType != NvDrsSetting.DwordType)
                continue;
            values[id] = new NvDrsValue(id, setting.CurrentValue, setting.IsCurrentPredefined != 0, (int)setting.SettingLocation);
        }
        return new(NvApiStatus.Ok, new NvDrsRead(Decode(description.ProfileName), values));
    }

    private NvResult<uint> ReadUInt32(nint gpu, uint functionId)
    {
        lock (_lock)
        {
            var get = (delegate* unmanaged[Cdecl]<nint, uint*, int>)Bound(functionId);
            if (get == null)
                return new(Missing, 0);

            uint value;
            var status = get(gpu, &value);
            return status == NvApiStatus.Ok ? new(status, value) : new(status, 0);
        }
    }

    private string ReadDriverVersion()
    {
        var get = (delegate* unmanaged[Cdecl]<uint*, NvShortString*, int>)Function(NvApiFunctionIds.SysGetDriverAndBranchVersion);
        if (get == null)
            return string.Empty;
        uint version;
        NvShortString branch;
        return get(&version, &branch) == NvApiStatus.Ok ? FormatDriverVersion(version) : string.Empty;
    }

    private string FullName(nint gpu)
    {
        var get = (delegate* unmanaged[Cdecl]<nint, NvShortString*, int>)Function(NvApiFunctionIds.GpuGetFullName);
        if (get == null)
            return string.Empty;
        NvShortString name;
        return get(gpu, &name) == NvApiStatus.Ok ? Decode(name) : string.Empty;
    }

    /// <summary>LUID адаптера Windows: через логический GPU, запасной путь — устаревший GPU_GetAdapterIdFromPhysicalGpu.</summary>
    private Reading<AdapterLuid> Luid(nint gpu)
    {
        var status = LuidFromLogicalGpu(gpu, out var luid);
        if (status == NvApiStatus.Ok)
            return Reading<AdapterLuid>.Known(luid, Source);

        var fallback = (delegate* unmanaged[Cdecl]<nint, NativeLuid*, int>)Function(NvApiFunctionIds.GpuGetAdapterIdFromPhysicalGpu);
        var native = default(NativeLuid);
        var fallbackStatus = fallback == null ? NvApiStatus.NoImplementation : fallback(gpu, &native);
        if (fallbackStatus == NvApiStatus.Ok)
            return Reading<AdapterLuid>.Known(new AdapterLuid(native.LowPart, native.HighPart), Source);
        return Reading<AdapterLuid>.Unknown(Source, string.Create(CultureInfo.InvariantCulture,
            $"NvAPI_GPU_GetLogicalGpuInfo failed: NVAPI status {status}; NvAPI_GPU_GetAdapterIdFromPhysicalGpu failed: NVAPI status {fallbackStatus}"));
    }

    private int LuidFromLogicalGpu(nint gpu, out AdapterLuid luid)
    {
        luid = default;
        var getLogical = (delegate* unmanaged[Cdecl]<nint, nint*, int>)Function(NvApiFunctionIds.GetLogicalGpuFromPhysicalGpu);
        var getInfo = (delegate* unmanaged[Cdecl]<nint, NvLogicalGpuData*, int>)Function(NvApiFunctionIds.GpuGetLogicalGpuInfo);
        if (getLogical == null || getInfo == null)
            return NvApiStatus.NoImplementation;

        nint logical;
        var status = getLogical(gpu, &logical);
        if (status != NvApiStatus.Ok)
            return status;
        var native = default(NativeLuid);
        var data = new NvLogicalGpuData { Version = NvLogicalGpuData.StructVersion, OsAdapterId = (nint)(&native) };
        status = getInfo(logical, &data);
        if (status == NvApiStatus.Ok)
            luid = new AdapterLuid(native.LowPart, native.HighPart);
        return status;
    }

    private Reading<PciId> Pci(nint gpu)
    {
        var get = (delegate* unmanaged[Cdecl]<nint, uint*, uint*, uint*, uint*, int>)Function(NvApiFunctionIds.GpuGetPciIdentifiers);
        uint deviceId = 0, subSystemId = 0, revisionId = 0, extDeviceId = 0;
        var status = get == null ? NvApiStatus.NoImplementation : get(gpu, &deviceId, &subSystemId, &revisionId, &extDeviceId);
        return status == NvApiStatus.Ok
            ? Reading<PciId>.Known(ToPciId(deviceId, subSystemId, revisionId), Source)
            : Reading<PciId>.Unknown(Source, string.Create(CultureInfo.InvariantCulture,
                $"NvAPI_GPU_GetPCIIdentifiers failed: NVAPI status {status}"));
    }

    /// <summary>Адрес функции из белого списка; после Dispose — 0.</summary>
    private nint Bound(uint functionId) => _disposed ? 0 : Function(functionId);

    private nint Function(uint functionId)
    {
        if (!_functions.TryGetValue(functionId, out var address))
        {
            address = _queryInterface(functionId);
            _functions[functionId] = address;
        }
        return address;
    }

    private static string Decode(in NvShortString text)
    {
        ReadOnlySpan<byte> bytes = text;
        var length = bytes.IndexOf((byte)0);
        return Encoding.Latin1.GetString(length < 0 ? bytes : bytes[..length]).Trim();
    }

    private static string Decode(in NvUnicodeString text)
    {
        ReadOnlySpan<char> chars = text;
        var length = chars.IndexOf('\0');
        return new string(length < 0 ? chars : chars[..length]);
    }

    /// <summary>LUID Windows: DWORD LowPart, LONG HighPart.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeLuid
    {
        public uint LowPart;
        public int HighPart;
    }
}

/// <summary>Создаёт <see cref="NvApiLibrary"/>; имя библиотеки подменяется только в тестах.</summary>
internal sealed class NvApiLibraryFactory : INvApiFactory
{
    private readonly string _libraryName;

    public NvApiLibraryFactory()
        : this(NvApiLibrary.LibraryName)
    {
    }

    internal NvApiLibraryFactory(string libraryName) => _libraryName = libraryName;

    public INvApi? TryCreate(out int status) => NvApiLibrary.TryLoad(_libraryName, out status);
}
