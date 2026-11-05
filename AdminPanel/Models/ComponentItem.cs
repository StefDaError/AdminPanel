namespace AdminPanel.Models;

public sealed class ComponentItem
{
    public required string Category { get; init; }
    public required string Name { get; init; }
    public required string Specs { get; init; }
}