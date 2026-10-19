using SmartXApi.Models;

namespace SmartXApi.Models
{
    public class TelemetryResult
    {
        public bool IsValid { get; set; }
        public string Message { get; set; } = string.Empty;
        public object? ProcessedValue { get; set; }
    }
    public class TelemetrySubmission
    {
        public int DeviceId { get; set; }
        public int SensorId { get; set; }
        public TelemetryValueType ValueType { get; set; }
        public string Value { get; set; } = string.Empty;

        public DateTime? Timestamp { get; set; }
    }
}