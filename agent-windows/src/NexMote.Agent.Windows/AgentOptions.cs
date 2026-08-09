namespace NexMote.Agent.Windows;

public sealed class AgentOptions
{
    public string ServerUrl { get; set; } = "http://localhost:5080";
    public string EnrollmentKey { get; set; } = "dev-enrollment-key";
    public string AgentVersion { get; set; } = "0.1.0";
    public string? LocationCode { get; set; }
    public int HeartbeatSeconds { get; set; } = 20;
}

