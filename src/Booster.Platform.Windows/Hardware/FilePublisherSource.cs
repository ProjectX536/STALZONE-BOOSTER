using System.Collections.Concurrent;
using System.Diagnostics;
using Booster.Core.Abstractions;

namespace Booster.Platform.Windows.Hardware;

internal interface IFilePublisherSource
{
    Reading<string> GetPublisher(string? path);
}

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
