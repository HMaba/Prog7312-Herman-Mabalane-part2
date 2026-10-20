using SmartXApi.Data;
using SmartXApi.Models;

namespace SmartXApi.Services
{

    public interface ITelemetryValidationService
    {
        Task<TelemetryResult> ValidateAsync(TelemetryPacketBase packet);
    }

    public class TelemetryValidationService : ITelemetryValidationService
    {
        private readonly IDataStore _store;

        public TelemetryValidationService(IDataStore store) => _store = store;

        public Task<TelemetryResult> ValidateAsync(TelemetryPacketBase packet) => Task.FromResult(Validate(packet));

        private TelemetryResult Validate(TelemetryPacketBase packet)
        {
            var device = _store.GetDevice(packet.DeviceId, includeChildren: false);
            if (device is null)
            {
                return new TelemetryResult { IsValid = false, Message = $"Device {packet.DeviceId} does not exist." };
            }

            var sensor = _store.GetSensor(packet.SensorId);
            if (sensor is not null && sensor.DeviceId != packet.DeviceId)
            {
                sensor = null;
            }

            if (sensor is null)
            {
                return new TelemetryResult { IsValid = false, Message = $"Sensor {packet.SensorId} is not registered to device {packet.DeviceId}." };
            }

            if (sensor.ValueType != packet.ValueType)
            {
                return new TelemetryResult
                {
                    IsValid = false,
                    Message = $"Sensor {sensor.Id} expects {sensor.ValueType} values but received {packet.ValueType}."
                };
            }

            if (packet.Timestamp > DateTime.UtcNow.AddMinutes(1))
            {
                return new TelemetryResult { IsValid = false, Message = "Telemetry timestamp is in the future." };
            }

            if (packet.GetValue() is null)
            {
                return new TelemetryResult { IsValid = false, Message = "Telemetry value cannot be null." };
            }

            return new TelemetryResult { IsValid = true, Message = "Valid", ProcessedValue = packet.GetValue() };
        }
    }
}