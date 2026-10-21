namespace Booster.Platform.Windows.Common;

/// <summary>
/// Пространства имён или класса WMI на этой системе нет. Отличается от прочих сбоев WMI тем, что повтор
/// не поможет: возможности нет, и вызывающий код переходит на запасной источник.
/// </summary>
public sealed class CimSchemaMissingException : Exception
{
    public CimSchemaMissingException()
    {
    }

    public CimSchemaMissingException(string message) : base(message)
    {
    }

    public CimSchemaMissingException(string message, Exception innerException) : base(message, innerException)
    {
    }

    public CimSchemaMissingException(string wmiNamespace, string message, Exception? innerException = null)
        : base($@"{wmiNamespace}: {message}", innerException)
    {
        WmiNamespace = wmiNamespace;
    }

    public string? WmiNamespace { get; }
}
