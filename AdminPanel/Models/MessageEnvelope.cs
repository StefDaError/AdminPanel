using System.Text.Json.Serialization;

namespace AdminPanel.Models;

public sealed record MessageEnvelope(
    [property: JsonPropertyName("v")] int Version,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("msg_id")] string MessageId,
    [property: JsonPropertyName("ts")] double Timestamp,
    [property: JsonPropertyName("agent_id")] string AgentId,
    [property: JsonPropertyName("payload")] HardwareInfo Payload);