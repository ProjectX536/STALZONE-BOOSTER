namespace Booster.Platform.Windows.Common;

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
