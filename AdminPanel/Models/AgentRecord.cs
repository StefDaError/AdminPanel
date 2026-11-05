namespace AdminPanel.Models;

public sealed class AgentRecord
{
    public required string NodeId { get; init; }
    public required string DomainName { get; init; }
    public string? IpAddress { get; init; }
    public string? Platform { get; init; }
    public string? OsVersion { get; init; }
    public DateTime? LastSeen { get; init; }
    public int SnapshotCount { get; init; }
}