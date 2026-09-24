namespace SecureTunnel.Client.Infrastructure.Storage;

public static class StorageErrorCodes
{
    public const string CorruptedData = "CorruptedData";
    public const string DecryptionFailed = "DecryptionFailed";
    public const string AtomicWriteFailed = "AtomicWriteFailed";
    public const string PathNotAllowed = "PathNotAllowed";
    public const string NotFound = "NotFound";
}
