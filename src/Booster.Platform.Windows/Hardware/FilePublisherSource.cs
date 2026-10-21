using System.Collections.Concurrent;
using System.Diagnostics;
using Booster.Core.Abstractions;

namespace Booster.Platform.Windows.Hardware;

/// <summary>Издатель исполняемого файла.</summary>
internal interface IFilePublisherSource
{
    Reading<string> GetPublisher(string? path);
}

/// <summary>
/// CompanyName из ресурса версии файла. Это не проверка подписи: строку пишет сам автор файла, поэтому она годится
/// только для того, чтобы НЕ предлагать службу к проверке, а не для доверия к ней.
/// </summary>
internal sealed class FilePublisherSource : IFilePublisherSource
{
    public const string Source = "FileVersionInfo.CompanyName";

    private readonly ConcurrentDictionary<string, Reading<string>> _cache = new(StringComparer.OrdinalIgnoreCase);

    public Reading<string> GetPublisher(string? path) =>
        string.IsNullOrWhiteSpace(path) ? Reading<string>.Unknown(Source, "No image path") : _cache.GetOrAdd(path, Read);

    private static Reading<string> Read(string path)
    {
        try
        {
            if (!File.Exists(path))
                return Reading<string>.Unknown(Source, "File not found");

            var company = FileVersionInfo.GetVersionInfo(path).CompanyName?.Trim();
            return string.IsNullOrEmpty(company) ? Reading<string>.Unknown(Source, "No CompanyName") : Reading<string>.Known(company, Source);
        }
        catch (Exception ex)
        {
            return Reading<string>.Failure(Source, $"{ex.GetType().Name}: {ex.Message}");
        }
    }
}
