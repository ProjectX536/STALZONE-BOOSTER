namespace Booster.Core.Profiles;

public sealed class ProfileFileRejectedException : InvalidOperationException
{
    public ProfileFileRejectedException()
    {
    }

    public ProfileFileRejectedException(string message) : base(message)
    {
    }

    public ProfileFileRejectedException(string message, Exception innerException) : base(message, innerException)
    {
    }

    public ProfileFileRejectedException(string fileName, string detail)
        : base($"{fileName}: user profile file was not applied ({detail}); it is not overwritten")
    {
        FileName = fileName;
    }

    public string? FileName { get; }
}
