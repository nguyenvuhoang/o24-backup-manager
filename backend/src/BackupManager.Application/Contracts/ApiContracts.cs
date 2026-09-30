namespace BackupManager.Application.Contracts;

public sealed record SecretMetadata(string Name, bool Configured, DateTimeOffset UpdatedAt);
public sealed record RunEvent(Guid RunId, Guid JobId, long Sequence, DateTimeOffset Timestamp, string Event, object? Data);
