using Microsoft.EntityFrameworkCore;
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
        private readonly SmartXDbContext _context;

        public TelemetryValidationService(SmartXDbContext context) => _context = context;

        public async Task<TelemetryResult> ValidateAsync(TelemetryPacketBase packet)
        {
            var device = await _context.Devices.AsNoTracking().FirstOrDefaultAsync(d => d.Id == packet.DeviceId);
            if (device is null)
            {
                return new TelemetryResult { IsValid = false, Message = $"Device {packet.DeviceId} does not exist." };
            }

            var sensor = await _context.Sensors.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == packet.SensorId && s.DeviceId == packet.DeviceId);
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