namespace SmartxWPF.Models;

public sealed class DashboardDto
{
    public DateTime GeneratedAt { get; set; }
    public StreamStatsDto Stats { get; set; } = new();
    public List<DeviceDto> Devices { get; set; } = new();
    public List<AlertDto> ActiveAlerts { get; set; } = new();
    public List<AlertDto> RecentAlerts { get; set; } = new();
    public List<CommandDto> RecentCommands { get; set; } = new();
    public List<InsightDto> Insights { get; set; } = new();
    public List<int> HighlightedDeviceIds { get; set; } = new();
}
public sealed class StreamStatsDto
{
    public int RegisteredDevices { get; set; }
    public int ActiveErrorStates { get; set; }
    public int DisconnectedNodes { get; set; }
    public long ProcessedPackets { get; set; }
}
public sealed class DeviceDto
{
    public int DeviceId { get; set; }
    public string DeviceIdentifier { get; set; } = "";
    public string MacAddress { get; set; } = "";
    public string NodeId { get; set; } = "";
    public string Location { get; set; } = "";
    public string Room { get; set; } = "";
    public string Status { get; set; } = "";
    public string AlertState { get; set; } = "";
    public long PacketsReceived { get; set; }
    public List<SensorDto> Sensors { get; set; } = new();
}
public sealed class SensorDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public string Unit { get; set; } = "";
    public object? Value { get; set; }
    public DateTime Timestamp { get; set; }
    public bool IsOutOfRange { get; set; }
}
public sealed class AlertDto
{
    public long Id { get; set; }
    public string Severity { get; set; } = "";
    public string DeviceIdentifier { get; set; } = "";
    public string AlertType { get; set; } = "";
    public string Message { get; set; } = "";
    public DateTime RaisedAt { get; set; }
}
public sealed class InsightDto
{
    public string Title { get; set; } = "";
    public string Reason { get; set; } = "";
    public string Message => string.IsNullOrWhiteSpace(Reason) ? Title : $"{Title} — {Reason}";
    public override string ToString() => Message;
}
public sealed class CommandDto
{
    public string DeviceIdentifier { get; set; } = "";
    public string SensorName { get; set; } = "";
    public string Command { get; set; } = "";
    public bool NewState { get; set; }
    public DateTime IssuedAt { get; set; }
    public bool Undone { get; set; }
}
public sealed class DeviceFilterDto { public string? Text { get; set; } }
public sealed class CommandRequestDto
{
    public int DeviceId { get; set; }
    public int? SensorId { get; set; }
    public int Command { get; set; }
}
public sealed class CommandOutcomeDto
{
    public string Message { get; set; } = "";
    public bool Success { get; set; }
    public string Kind { get; set; } = "";
}
public sealed class DeviceImportResultDto
{
    public int Created { get; set; }
    public int Updated { get; set; }
    public int Skipped { get; set; }
    public List<ImportLineErrorDto> Errors { get; set; } = new();
    public List<DeviceDto> Devices { get; set; } = new();
    public int LinesRead { get; set; }
    public string FileName { get; set; } = "";
}
public sealed class ImportLineErrorDto { public int Line { get; set; } public string Message { get; set; } = ""; }
public sealed class InsightsResponseDto { public List<InsightDto> Suggestions { get; set; } = new(); public int RecordedActions { get; set; }
}
public sealed class SensorTimelineDto { public DateTime Timestamp { get; set; } public object? Value { get; set; } public double? Numeric { get; set; } }
