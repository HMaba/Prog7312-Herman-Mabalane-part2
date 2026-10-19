using SmartXApi.Models;
using System.Text.Json;

namespace SmartXApi.Models
{

    public abstract class TelemetryPacketBase
    {
        public int DeviceId { get; set; }
        public int SensorId { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        public abstract object GetValue();

        public abstract TelemetryValueType ValueType { get; }
    }

    public class TelemetryPacket<T> : TelemetryPacketBase
    {
        public T Value { get; set; } = default!;

        public override object GetValue() => Value!;

        public override TelemetryValueType ValueType => typeof(T) switch
        {
            Type t when t == typeof(float) || t == typeof(double) => TelemetryValueType.Float,
            Type t when t == typeof(int) => TelemetryValueType.Int,
            Type t when t == typeof(bool) => TelemetryValueType.Bool,
            _ => TelemetryValueType.String
        };

        public override string ToString() => $"[Device {DeviceId} / Sensor {SensorId}] {Value} @ {Timestamp:O}";
    }

    public class TelemetryRecord
    {
        public int Id { get; set; }
        public int DeviceId { get; set; }
        public int SensorId { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public TelemetryValueType ValueType { get; set; }

        public string ValueJson { get; set; } = string.Empty;

        public static TelemetryRecord FromPacket(TelemetryPacketBase packet) => new()
        {
            DeviceId = packet.DeviceId,
            SensorId = packet.SensorId,
            Timestamp = packet.Timestamp,
            ValueType = packet.ValueType,
            ValueJson = JsonSerializer.Serialize(packet.GetValue())
        };

        public TelemetryPacketBase ToPacket()
        {
            return ValueType switch
            {
                TelemetryValueType.Float => new TelemetryPacket<float>
                {
                    DeviceId = DeviceId,
                    SensorId = SensorId,
                    Timestamp = Timestamp,
                    Value = JsonSerializer.Deserialize<float>(ValueJson)
                },
                TelemetryValueType.Int => new TelemetryPacket<int>
                {
                    DeviceId = DeviceId,
                    SensorId = SensorId,
                    Timestamp = Timestamp,
                    Value = JsonSerializer.Deserialize<int>(ValueJson)
                },
                TelemetryValueType.Bool => new TelemetryPacket<bool>
                {
                    DeviceId = DeviceId,
                    SensorId = SensorId,
                    Timestamp = Timestamp,
                    Value = JsonSerializer.Deserialize<bool>(ValueJson)
                },
                _ => new TelemetryPacket<string>
                {
                    DeviceId = DeviceId,
                    SensorId = SensorId,
                    Timestamp = Timestamp,
                    Value = JsonSerializer.Deserialize<string>(ValueJson) ?? string.Empty
                }
            };
        }
    }
}