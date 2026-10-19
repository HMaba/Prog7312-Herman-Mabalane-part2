using SmartXApi.Models;
using System.Text.Json.Serialization;

namespace SmartXApi.Models
{
    public enum AlertSeverity
    {
        Critical = 0,
        High = 1,
        Warning = 2,
        Info = 3
    }

    public enum DeviceStatus
    {
        AwaitingData,
        Online,
        Disconnected
    }

    public enum DeviceAlertState
    {
        Normal,
        Warning,
        Critical,
        Disconnected,
        Alerting
    }

    public enum OverrideCommandType
    {
        TurnOn,
        TurnOff,
        Toggle
    }

    public enum CommandOutcomeKind
    {
        Success,
        NotFound,
        Invalid,
        Conflict,
        NothingToUndo
    }

    public enum UserActionKind
    {
        Search,
        Command,
        Undo
    }

    public class SensorInfo
    {
        public int Id { get; set; }
        public int DeviceId { get; set; }
        public string Name { get; set; } = string.Empty;
        public SensorCategory Category { get; set; }
        public TelemetryValueType ValueType { get; set; }
        public string Unit { get; set; } = string.Empty;
    }

    public class SensorReading
    {
        public int SensorId { get; set; }
        public object? Value { get; set; }
        public double? Numeric { get; set; }
        public DateTime Timestamp { get; set; }
        public DateTime ReceivedAt { get; set; }
    }

    public class SensorTimelineEntry
    {
        public DateTime Timestamp { get; set; }
        public object? Value { get; set; }
        public double? Numeric { get; set; }
    }

    public class DeviceState
    {
        public int DeviceId { get; set; }
        public string RegistryKey { get; set; } = string.Empty;
        public string DeviceIdentifier { get; set; } = string.Empty;
        public string MacAddress { get; set; } = string.Empty;
        public string NodeId { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
        public string Room { get; set; } = string.Empty;
        public Dictionary<int, SensorInfo> Sensors { get; set; } = new();
        public Dictionary<int, SensorReading> LatestReadings { get; } = new();
        public Dictionary<string, AlertSeverity> ActiveAlerts { get; } = new();
        public DateTime? LastSeenUtc { get; set; }
        public long PacketsReceived { get; set; }

        public bool TryGetBool(int sensorId, out bool value)
        {
            if (LatestReadings.TryGetValue(sensorId, out var reading) && reading.Value is bool flag)
            {
                value = flag;
                return true;
            }

            value = false;
            return false;
        }
    }

    public class TelemetryEnvelope
    {
        public TelemetryPacketBase Packet { get; set; } = null!;
        public DateTime ReceivedAt { get; set; }
        public long Sequence { get; set; }
    }

    public class StreamAlert
    {
        public long Id { get; set; }
        public string Signature { get; set; } = string.Empty;
        public string AlertType { get; set; } = string.Empty;
        public AlertSeverity Severity { get; set; }
        public int DeviceId { get; set; }
        public string DeviceIdentifier { get; set; } = string.Empty;
        public string MacAddress { get; set; } = string.Empty;
        public int? SensorId { get; set; }
        public string? SensorName { get; set; }
        public string Message { get; set; } = string.Empty;
        public double? Value { get; set; }
        public DateTime RaisedAt { get; set; }
        public DateTime? ProcessedAt { get; set; }
        public bool BypassedQueue { get; set; }

        [JsonIgnore]
        public HashSet<string> CreditedActions { get; } = new();
    }

    public class OverrideCommand
    {
        public long Id { get; set; }
        public int DeviceId { get; set; }
        public string DeviceIdentifier { get; set; } = string.Empty;
        public int SensorId { get; set; }
        public string SensorName { get; set; } = string.Empty;
        public OverrideCommandType Command { get; set; }
        public bool PreviousState { get; set; }
        public bool PreviousStateKnown { get; set; }
        public bool NewState { get; set; }
        public string IssuedBy { get; set; } = string.Empty;
        public DateTime IssuedAt { get; set; }
        public bool IsUndo { get; set; }
        public long? UndoOfCommandId { get; set; }
        public bool Undone { get; set; }
    }

    public class UserActionEvent
    {
        public long Id { get; set; }
        public DateTime Timestamp { get; set; }
        public string User { get; set; } = string.Empty;
        public UserActionKind Kind { get; set; }
        public string ActionKey { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }

    public class PatternStat
    {
        public string Trigger { get; set; } = string.Empty;
        public string ActionKey { get; set; } = string.Empty;
        public int Count { get; set; }
        public DateTime LastSeen { get; set; }
    }

    public class SuggestedAction
    {
        public string Type { get; set; } = string.Empty;
        public int? DeviceId { get; set; }
        public int? SensorId { get; set; }
        public OverrideCommandType? Command { get; set; }
        public string? SearchText { get; set; }
        public SensorCategory? Category { get; set; }
        public DeviceAlertState? AlertState { get; set; }
        public DeviceStatus? Status { get; set; }
    }

    public class AutomatedInsight
    {
        public string Id { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public AlertSeverity Severity { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
        public double Confidence { get; set; }
        public int Support { get; set; }
        public int? DeviceId { get; set; }
        public int? SensorId { get; set; }
        public string? DeviceIdentifier { get; set; }
        public SuggestedAction? Action { get; set; }
        public DateTime GeneratedAt { get; set; }
    }

    public class LearnedPattern
    {
        public string Trigger { get; set; } = string.Empty;
        public string TriggerLabel { get; set; } = string.Empty;
        public string ActionKey { get; set; } = string.Empty;
        public string ActionDescription { get; set; } = string.Empty;
        public int Count { get; set; }
        public int TriggerOccurrences { get; set; }
        public double Confidence { get; set; }
        public DateTime LastSeen { get; set; }
        public bool IsTriggerActive { get; set; }
    }

    public class InsightsResponse
    {
        public DateTime GeneratedAt { get; set; }
        public List<AutomatedInsight> Suggestions { get; set; } = new();
        public List<LearnedPattern> LearnedPatterns { get; set; } = new();
        public List<int> HighlightedDeviceIds { get; set; } = new();
        public List<UserActionEvent> RecentActivity { get; set; } = new();
        public int RecordedActions { get; set; }
    }

    public class SensorSnapshot
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public SensorCategory Category { get; set; }
        public TelemetryValueType ValueType { get; set; }
        public string Unit { get; set; } = string.Empty;
        public object? Value { get; set; }
        public DateTime? Timestamp { get; set; }
        public bool IsOutOfRange { get; set; }
        public string? ActiveAlertType { get; set; }
        public AlertSeverity? ActiveAlertSeverity { get; set; }
    }

    public class DeviceSnapshot
    {
        public int DeviceId { get; set; }
        public string DeviceIdentifier { get; set; } = string.Empty;
        public string MacAddress { get; set; } = string.Empty;
        public string NodeId { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
        public string Room { get; set; } = string.Empty;
        public DeviceStatus Status { get; set; }
        public DeviceAlertState AlertState { get; set; }
        public List<SensorCategory> Categories { get; set; } = new();
        public List<SensorSnapshot> Sensors { get; set; } = new();
        public DateTime? LastSeenUtc { get; set; }
        public long PacketsReceived { get; set; }
    }

    public class StreamStats
    {
        public int RegisteredDevices { get; set; }
        public int TelemetryQueueDepth { get; set; }
        public int CriticalQueueDepth { get; set; }
        public int UndoStackDepth { get; set; }
        public int ActiveErrorStates { get; set; }
        public int DisconnectedNodes { get; set; }
        public long ProcessedPackets { get; set; }
        public long CriticalAlertsProcessed { get; set; }
        public long CriticalReadingsBypassed { get; set; }
        public long DuplicateAlertsSuppressed { get; set; }
        public long UnknownDevicePackets { get; set; }
    }

    public class DashboardSnapshot
    {
        public long Version { get; set; }
        public DateTime GeneratedAt { get; set; }
        public StreamStats Stats { get; set; } = new();
        public List<DeviceSnapshot> Devices { get; set; } = new();
        public List<StreamAlert> ActiveAlerts { get; set; } = new();
        public List<StreamAlert> RecentAlerts { get; set; } = new();
        public List<OverrideCommand> RecentCommands { get; set; } = new();
        public OverrideCommand? NextUndo { get; set; }
        public List<AutomatedInsight> Insights { get; set; } = new();
        public List<int> HighlightedDeviceIds { get; set; } = new();
    }

    public class CommandRequest
    {
        public int DeviceId { get; set; }
        public int? SensorId { get; set; }
        public OverrideCommandType Command { get; set; }
    }

    public class CommandOutcome
    {
        public CommandOutcomeKind Kind { get; set; }
        public bool Success => Kind == CommandOutcomeKind.Success;
        public string Message { get; set; } = string.Empty;
        public OverrideCommand? Command { get; set; }
    }

    public class DeviceFilter
    {
        public string? Text { get; set; }
        public SensorCategory? Category { get; set; }
        public DeviceAlertState? AlertState { get; set; }
        public DeviceStatus? Status { get; set; }

        [JsonIgnore]
        public bool IsEmpty =>
            string.IsNullOrWhiteSpace(Text) && !Category.HasValue && !AlertState.HasValue && !Status.HasValue;
    }

    public class BatchItemResult
    {
        public int Index { get; set; }
        public bool IsValid { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    public class BatchIngestResult
    {
        public int Accepted { get; set; }
        public int Rejected { get; set; }
        public List<BatchItemResult> Results { get; set; } = new();
    }

    public class ImportLineError
    {
        public int Line { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    public class DeviceImportResult
    {
        public string FileName { get; set; } = string.Empty;
        public int LinesRead { get; set; }
        public int Created { get; set; }
        public int Updated { get; set; }
        public int Skipped { get; set; }
        public List<ImportLineError> Errors { get; set; } = new();
        public List<DeviceSnapshot> Devices { get; set; } = new();
    }
}
