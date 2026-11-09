namespace PrivacyGuard.Core;

/// <summary>Video review approvals must bind to the exact source bytes, not only metadata.</summary>
public static class VideoReviewBinding
{
    public const int CurrentSchemaVersion = 2;

    public static bool IsWellFormedSha256(string? sha) =>
        sha is { Length: 64 } && sha.All(c => Uri.IsHexDigit(c));

    public static bool CanTrustApprovals(int schemaVersion, string? storedHash, string actualHash) =>
        schemaVersion == CurrentSchemaVersion &&
        IsWellFormedSha256(storedHash) && IsWellFormedSha256(actualHash) &&
        string.Equals(storedHash, actualHash, StringComparison.OrdinalIgnoreCase);

    public static bool IsLegacy(int schemaVersion) => schemaVersion == 1;
}
