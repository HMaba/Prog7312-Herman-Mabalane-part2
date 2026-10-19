using SmartXApi.Models;
using System.Globalization;

namespace SmartXApi.Services
{
    public static class TelemetryPacketFactory
    {
        private const int MaxStringLength = 256;

        public static bool TryCreate(TelemetrySubmission submission, out TelemetryPacketBase? packet, out string error)
        {
            packet = null;
            error = string.Empty;

            var timestamp = NormalizeTimestamp(submission.Timestamp);
            var raw = (submission.Value ?? string.Empty).Trim();

            switch (submission.ValueType)
            {
                case TelemetryValueType.Float:
                    if (!float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var floatValue) || !float.IsFinite(floatValue))
                    {
                        error = $"'{Truncate(raw)}' is not a valid Float value.";
                        return false;
                    }

                    packet = new TelemetryPacket<float> { DeviceId = submission.DeviceId, SensorId = submission.SensorId, Timestamp = timestamp, Value = floatValue };
                    return true;

                case TelemetryValueType.Int:
                    if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intValue))
                    {
                        error = $"'{Truncate(raw)}' is not a valid Int value.";
                        return false;
                    }

                    packet = new TelemetryPacket<int> { DeviceId = submission.DeviceId, SensorId = submission.SensorId, Timestamp = timestamp, Value = intValue };
                    return true;

                case TelemetryValueType.Bool:
                    if (!bool.TryParse(raw, out var boolValue))
                    {
                        error = $"'{Truncate(raw)}' is not a valid Bool value.";
                        return false;
                    }

                    packet = new TelemetryPacket<bool> { DeviceId = submission.DeviceId, SensorId = submission.SensorId, Timestamp = timestamp, Value = boolValue };
                    return true;

                case TelemetryValueType.String:
                    if (raw.Length > MaxStringLength)
                    {
                        error = $"String values cannot exceed {MaxStringLength} characters.";
                        return false;
                    }

                    packet = new TelemetryPacket<string> { DeviceId = submission.DeviceId, SensorId = submission.SensorId, Timestamp = timestamp, Value = raw };
                    return true;

                default:
                    error = "Unsupported value type.";
                    return false;
            }
        }

        private static DateTime NormalizeTimestamp(DateTime? value)
        {
            if (!value.HasValue)
            {
                return DateTime.UtcNow;
            }

            return value.Value.Kind switch
            {
                DateTimeKind.Local => value.Value.ToUniversalTime(),
                DateTimeKind.Unspecified => DateTime.SpecifyKind(value.Value, DateTimeKind.Utc),
                _ => value.Value
            };
        }

        private static string Truncate(string value) => value.Length <= 40 ? value : value[..40] + "...";
    }
}
