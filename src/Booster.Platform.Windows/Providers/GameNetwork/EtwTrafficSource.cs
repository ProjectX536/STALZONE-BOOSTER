using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Booster.Core.Abstractions;
using Booster.Core.Configuration;
using Microsoft.Extensions.Logging;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Diagnostics.Etw;

namespace Booster.Platform.Windows.Providers.GameNetwork;

/// <summary>Трафик по процессам из real-time сессии ETW с провайдером Microsoft-Windows-Kernel-Network.</summary>
public sealed unsafe partial class EtwTrafficSource(IBoosterConfiguration config, ILogger<EtwTrafficSource> logger) : ITrafficSource
{
    public const string SessionName = "Booster-GameNetwork";

    private const string Source = "ETW";

    // Wnode.ClientContext = 1: метки времени событий по QueryPerformanceCounter.
    private const uint QpcClockContext = 1;

    // Сброс буферов раз в секунду: события доходят до счётчиков в пределах периода опроса 1–2 с.
    private const uint FlushTimerSeconds = 1;

    // TRACE_LEVEL_INFORMATION: на этом уровне Kernel-Network пишет события передачи данных.
    private const byte InformationLevel = 4;

    // MatchAllKeyword = 0: достаточно совпадения с любым битом Keywords.
    private const ulong MatchAllKeywords = 0;

    // Timeout = 0: EnableTraceEx2 не ждёт колбэков включения провайдера.
    private const uint EnableWithoutWaiting = 0;

    // ETW ограничивает имя сессии и путь файла журнала 1024 символами каждое.
    private const int MaxNameChars = 1024;

    private const string ProcessThreadName = "Booster ETW";

    // После остановки сессии ProcessTrace дочитывает буферы и возвращается за доли секунды; дольше задерживать конец замера незачем.
    private static readonly TimeSpan ProcessTraceExitWait = TimeSpan.FromSeconds(2);

    // INVALID_PROCESSTRACE_HANDLE = (UINT64)UINTPTR_MAX.
    private static readonly ulong InvalidProcessTraceHandle = nuint.MaxValue;

    // Имя сессии лежит сразу за структурой, за ним место под путь файла журнала (его ETW пишет при остановке).
    private static readonly uint LoggerNameOffset = (uint)sizeof(EVENT_TRACE_PROPERTIES);
    private static readonly uint LogFileNameOffset = LoggerNameOffset + MaxNameChars * sizeof(char);
    private static readonly uint PropertiesBufferBytes = LogFileNameOffset + MaxNameChars * sizeof(char);

    public Reading<ITrafficSession> Start()
    {
        HashSet<IPAddress> localAddresses;
        try
        {
            localAddresses = LocalUnicastAddresses();
        }
        catch (NetworkInformationException ex)
        {
            LogLocalAddressesFailed(logger, ex.Message);
            return Reading<ITrafficSession>.Failure(Source, "local addresses are unavailable");
        }

        var session = new EtwTrafficSession(
            new TrafficFlowAggregator(config.Current.Settings.GameNetwork.MaxTrackedFlows, localAddresses), logger);
        var started = false;
        try
        {
            var error = StartSession(out var controlHandle);
            if (error == WIN32_ERROR.ERROR_ALREADY_EXISTS)
            {
                StopStaleSession();
                error = StartSession(out controlHandle);
            }

            if (error != WIN32_ERROR.ERROR_SUCCESS)
            {
                LogStartTraceFailed(logger, (uint)error);
                return MapStartError((uint)error);
            }

            string? failure;
            try
            {
                failure = session.Attach(controlHandle);
            }
            catch (Exception ex)
            {
                LogAttachThrew(logger, ex.GetType().Name, ex.Message);
                return Reading<ITrafficSession>.Failure(Source, ex.GetType().Name + " after StartTrace");
            }

            if (failure is not null)
            {
                LogAttachFailed(logger, failure);
                return Reading<ITrafficSession>.Failure(Source, failure);
            }

            started = true;
            return Reading<ITrafficSession>.Known(session, Source);
        }
        finally
        {
            if (!started)
                session.Dispose();
        }
    }

    public void StopStaleSession()
    {
        var error = StopSession(0, SessionName, out _, out _);
        if (error == WIN32_ERROR.ERROR_WMI_INSTANCE_NOT_FOUND)
            return;
        if (IsStopped(error))
            LogStaleSessionStopped(logger);
        else
            LogStopFailed(logger, (uint)error);
    }

    public static Reading<ITrafficSession> MapStartError(uint win32Error)
    {
        var note = Describe("StartTrace", win32Error);
        return (WIN32_ERROR)win32Error switch
        {
            WIN32_ERROR.ERROR_ACCESS_DENIED => Reading<ITrafficSession>.NeedsAdmin(Source, note),
            WIN32_ERROR.ERROR_NO_SYSTEM_RESOURCES => Reading<ITrafficSession>.Unknown(Source, note + ": no free ETW sessions"),
            _ => Reading<ITrafficSession>.Failure(Source, note),
        };
    }

    private static HashSet<IPAddress> LocalUnicastAddresses() =>
    [
        .. NetworkInterface.GetAllNetworkInterfaces()
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Select(a => a.Address),
    ];

    private static WIN32_ERROR StartSession(out ulong controlHandle)
    {
        // Wnode.Guid = GUID_NULL: ETW сам создаёт GUID сессии. BufferSize, MinimumBuffers, MaximumBuffers = 0: системные значения.
        // LogFileNameOffset = 0: сессия только real-time, без файла.
        var properties = AllocateProperties();
        try
        {
            properties->Wnode.Flags = PInvoke.WNODE_FLAG_TRACED_GUID;
            properties->Wnode.ClientContext = QpcClockContext;
            properties->LogFileMode = PInvoke.EVENT_TRACE_REAL_TIME_MODE;
            properties->FlushTimer = FlushTimerSeconds;

            ulong handle = 0;
            WIN32_ERROR error;
            fixed (char* name = SessionName)
                error = PInvoke.StartTrace(&handle, name, properties);
            controlHandle = handle;
            return error;
        }
        finally
        {
            NativeMemory.Free(properties);
        }
    }

    /// <summary>Останавливает сессию по дескриптору или, если он 0, по имени. Потери — из итоговых свойств сессии.</summary>
    private static WIN32_ERROR StopSession(ulong controlHandle, string? name, out uint eventsLost, out uint realTimeBuffersLost)
    {
        var properties = AllocateProperties();
        try
        {
            properties->LogFileNameOffset = LogFileNameOffset;
            WIN32_ERROR error;
            fixed (char* namePointer = name)
                error = PInvoke.ControlTrace(controlHandle, namePointer, properties, EVENT_TRACE_CONTROL.EVENT_TRACE_CONTROL_STOP);
            eventsLost = properties->EventsLost;
            realTimeBuffersLost = properties->RealTimeBuffersLost;
            return error;
        }
        finally
        {
            NativeMemory.Free(properties);
        }
    }

    private static EVENT_TRACE_PROPERTIES* AllocateProperties()
    {
        var properties = (EVENT_TRACE_PROPERTIES*)NativeMemory.AllocZeroed(PropertiesBufferBytes);
        properties->Wnode.BufferSize = PropertiesBufferBytes;
        properties->LoggerNameOffset = LoggerNameOffset;
        return properties;
    }

    // ERROR_MORE_DATA при остановке: сессия уже остановлена, не поместились только её свойства.
    private static bool IsStopped(WIN32_ERROR error) => error is WIN32_ERROR.ERROR_SUCCESS or WIN32_ERROR.ERROR_MORE_DATA;

    private static string Describe(string function, uint win32Error) =>
        string.Create(CultureInfo.InvariantCulture, $"{function} error {win32Error}");

    [LoggerMessage(Level = LogLevel.Warning, Message = "ETW traffic: local addresses were not read: {Message}")]
    private static partial void LogLocalAddressesFailed(ILogger logger, string message);

    [LoggerMessage(Level = LogLevel.Warning, Message = "ETW traffic: StartTrace failed with error {Error}")]
    private static partial void LogStartTraceFailed(ILogger logger, uint error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "ETW traffic: session is stopped after {Detail}")]
    private static partial void LogAttachFailed(ILogger logger, string detail);

    [LoggerMessage(Level = LogLevel.Warning, Message = "ETW traffic: session is stopped after {ExceptionType}: {Message}")]
    private static partial void LogAttachThrew(ILogger logger, string exceptionType, string message);

    [LoggerMessage(Level = LogLevel.Information, Message = "ETW traffic: stale session was stopped")]
    private static partial void LogStaleSessionStopped(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "ETW traffic: session was not stopped, ControlTrace error {Error}")]
    private static partial void LogStopFailed(ILogger logger, uint error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "ETW traffic: CloseTrace failed with error {Error}")]
    private static partial void LogCloseTraceFailed(ILogger logger, uint error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "ETW traffic: ProcessTrace ended with error {Error}")]
    private static partial void LogProcessTraceFailed(ILogger logger, uint error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "ETW traffic: ProcessTrace ended before the session was closed, code {Error}")]
    private static partial void LogProcessTraceEndedEarly(ILogger logger, uint error);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "ETW traffic: ProcessTrace did not exit within {Seconds} s; its callback context is kept alive")]
    private static partial void LogProcessTraceStuck(ILogger logger, double seconds);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "ETW traffic: session lost {EventsLost} events and {RealTimeBuffersLost} real-time buffers")]
    private static partial void LogEventsLost(ILogger logger, uint eventsLost, uint realTimeBuffersLost);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "ETW traffic: event callback failed, later failures are only counted: {ExceptionType}: {Message}")]
    private static partial void LogCallbackFailed(ILogger logger, string exceptionType, string message);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "ETW traffic: event {EventId} version {Version} ({Length} bytes) is not supported; such events are counted as dropped")]
    private static partial void LogUnsupportedEvent(ILogger logger, ushort eventId, byte version, int length);

    private sealed class EtwTrafficSession : ITrafficSession
    {
        private readonly TrafficFlowAggregator _aggregator;
        private readonly ILogger _logger;

        // Контекст колбэка и копия имени для OpenTrace живут, пока не завершился поток ProcessTrace.
        private GCHandle _self;
        private readonly nint _loggerName;

        private ulong _controlHandle;
        private PROCESSTRACE_HANDLE _traceHandle = new(InvalidProcessTraceHandle);
        private Thread? _thread;
        private long _callbackErrors;
        private int _callbackErrorLogged;
        private long _unsupportedEvents;
        private int _unsupportedEventLogged;
        private int _disposed;

        public EtwTrafficSession(TrafficFlowAggregator aggregator, ILogger logger)
        {
            _aggregator = aggregator;
            _logger = logger;
            _self = GCHandle.Alloc(this);
            _loggerName = Marshal.StringToHGlobalUni(SessionName);
        }

        public long DroppedEvents =>
            _aggregator.DroppedEvents + Interlocked.Read(ref _callbackErrors) + Interlocked.Read(ref _unsupportedEvents);

        public IReadOnlyList<TrafficFlow> ReadTotals() => _aggregator.Snapshot();

        /// <summary>Включает провайдер и запускает чтение событий. Null — успех, иначе описание ошибки.</summary>
        public string? Attach(ulong controlHandle)
        {
            _controlHandle = controlHandle;

            var providerId = KernelNetworkEvents.ProviderId;
            var error = PInvoke.EnableTraceEx2(controlHandle, &providerId,
                (uint)ENABLECALLBACK_ENABLED_STATE.EVENT_CONTROL_CODE_ENABLE_PROVIDER, InformationLevel,
                KernelNetworkEvents.Keywords, MatchAllKeywords, EnableWithoutWaiting, null);
            if (error != WIN32_ERROR.ERROR_SUCCESS)
                return Describe("EnableTraceEx2", (uint)error);

            var logfile = new EVENT_TRACE_LOGFILEW
            {
                LoggerName = (char*)_loggerName,
                Context = (void*)GCHandle.ToIntPtr(_self),
            };
            logfile.ProcessTraceMode = PInvoke.PROCESS_TRACE_MODE_REAL_TIME | PInvoke.PROCESS_TRACE_MODE_EVENT_RECORD;
            logfile.Anonymous2.EventRecordCallback = &OnEvent;
            var traceHandle = PInvoke.OpenTrace(ref logfile);
            if (traceHandle.Value == InvalidProcessTraceHandle)
                return Describe("OpenTrace", (uint)Marshal.GetLastPInvokeError());
            _traceHandle = traceHandle;

            var thread = new Thread(Process) { IsBackground = true, Name = ProcessThreadName };
            thread.Start();
            _thread = thread;
            return null;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            if (_controlHandle != 0)
            {
                var error = StopSession(_controlHandle, null, out var eventsLost, out var realTimeBuffersLost);
                if (IsStopped(error))
                {
                    if (eventsLost != 0 || realTimeBuffersLost != 0)
                        LogEventsLost(_logger, eventsLost, realTimeBuffersLost);
                }
                else if (error != WIN32_ERROR.ERROR_WMI_INSTANCE_NOT_FOUND)
                {
                    LogStopFailed(_logger, (uint)error);
                }
            }

            if (_traceHandle.Value != InvalidProcessTraceHandle)
            {
                // ERROR_CTX_CLOSE_PENDING: ProcessTrace дочитает уже полученные буферы и вернётся.
                var error = PInvoke.CloseTrace(_traceHandle);
                if (error is not (WIN32_ERROR.ERROR_SUCCESS or WIN32_ERROR.ERROR_CTX_CLOSE_PENDING))
                    LogCloseTraceFailed(_logger, (uint)error);
            }

            if (_thread is not null && !_thread.Join(ProcessTraceExitWait))
            {
                // Колбэк ещё может прийти: лучше небольшая утечка, чем обращение к освобождённому контексту.
                LogProcessTraceStuck(_logger, ProcessTraceExitWait.TotalSeconds);
                return;
            }

            _self.Free();
            Marshal.FreeHGlobal(_loggerName);
        }

        private void Process()
        {
            var handle = _traceHandle;
            var error = PInvoke.ProcessTrace(&handle, 1, null, null);
            if (Volatile.Read(ref _disposed) == 0)
                LogProcessTraceEndedEarly(_logger, (uint)error);
            else if (error is not (WIN32_ERROR.ERROR_SUCCESS or WIN32_ERROR.ERROR_CANCELLED))
                LogProcessTraceFailed(_logger, (uint)error);
        }

        // Событие о трафике, которое не разобрано (другая версия манифеста, короткие данные), не должно стать «трафика нет».
        private void CountUnsupported(ushort eventId, byte version, int length)
        {
            Interlocked.Increment(ref _unsupportedEvents);
            if (Interlocked.CompareExchange(ref _unsupportedEventLogged, 1, 0) == 0)
                LogUnsupportedEvent(_logger, eventId, version, length);
        }

        private void CountCallbackError(Exception ex)
        {
            Interlocked.Increment(ref _callbackErrors);
            if (Interlocked.CompareExchange(ref _callbackErrorLogged, 1, 0) != 0)
                return;
            try
            {
                LogCallbackFailed(_logger, ex.GetType().Name, ex.Message);
            }
            catch (Exception)
            {
                // Сбой журнала не должен выйти из колбэка ETW.
            }
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
        private static void OnEvent(EVENT_RECORD* record)
        {
            if (record->EventHeader.ProviderId != KernelNetworkEvents.ProviderId || record->UserContext is null)
                return;

            EtwTrafficSession? session = null;
            try
            {
                session = (EtwTrafficSession)GCHandle.FromIntPtr((nint)record->UserContext).Target!;
                var descriptor = record->EventHeader.EventDescriptor;
                var data = new ReadOnlySpan<byte>(record->UserData, record->UserDataLength);
                if (KernelNetworkEvents.TryParse(descriptor.Id, descriptor.Version, data, out var e))
                    session._aggregator.Add(e);
                else if (KernelNetworkEvents.IsTrafficEventId(descriptor.Id))
                    session.CountUnsupported(descriptor.Id, descriptor.Version, data.Length);
            }
            catch (Exception ex)
            {
                // Исключение из колбэка, вызванного из нативного кода, завершило бы процесс.
                session?.CountCallbackError(ex);
            }
        }
    }
}
