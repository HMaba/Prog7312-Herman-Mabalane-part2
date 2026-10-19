using System.Diagnostics.CodeAnalysis;
using SmartXApi.Models;

namespace SmartXApi.Services
{
    public class StreamDataStructures
    {
        public const int MaxTimelinePoints = 500;
        public const int MaxAlertLog = 500;
        public const int MaxCommandHistory = 500;
        public const int MaxUserActivity = 1000;

        private long _version;
        private long _sequence;

        public object SyncRoot { get; } = new();

        public Queue<TelemetryEnvelope> TelemetryQueue { get; } = new();

        public PriorityQueue<StreamAlert, (int Severity, long Sequence)> CriticalAlertQueue { get; } = new();

        public Stack<OverrideCommand> CommandUndoStack { get; } = new();

        public Dictionary<string, DeviceState> DeviceRegistry { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<int, string> DeviceIdIndex { get; } = new();

        public Dictionary<(int DeviceId, int SensorId), SortedDictionary<DateTime, SensorTimelineEntry>> SensorTimeline { get; } = new();

        public HashSet<string> DisconnectedNodes { get; } = new(StringComparer.OrdinalIgnoreCase);

        public HashSet<string> ActiveErrorStates { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, StreamAlert> ActiveAlertIndex { get; } = new(StringComparer.Ordinal);

        public List<StreamAlert> AlertLog { get; } = new();

        public List<OverrideCommand> CommandHistory { get; } = new();

        public List<UserActionEvent> UserActivity { get; } = new();

        public Dictionary<string, int> TriggerOccurrences { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, string> TriggerLabels { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, PatternStat> TriggerActionPatterns { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, Dictionary<string, int>> ActionTransitions { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, SuggestedAction> ActionPayloads { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, (string ActionKey, DateTime At)> LastActionByUser { get; } = new(StringComparer.Ordinal);

        public long ProcessedPackets { get; set; }

        public long CriticalAlertsProcessed { get; set; }

        public long CriticalReadingsBypassed { get; set; }

        public long DuplicateAlertsSuppressed { get; set; }

        public long UnknownDevicePackets { get; set; }

        public long Version => Interlocked.Read(ref _version);

        public void Touch() => Interlocked.Increment(ref _version);

        public long NextSequence() => Interlocked.Increment(ref _sequence);

        public bool TryGetDevice(int deviceId, [NotNullWhen(true)] out DeviceState? state)
        {
            state = null;
            return DeviceIdIndex.TryGetValue(deviceId, out var key) && DeviceRegistry.TryGetValue(key, out state);
        }
    }
}
