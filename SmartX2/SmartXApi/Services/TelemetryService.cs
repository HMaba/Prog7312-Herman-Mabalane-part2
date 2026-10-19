using Microsoft.EntityFrameworkCore;
using SmartXApi.Data;
using SmartXApi.Models;
using SmartXApi.Services;

namespace SmartXApi.Services
{
    public interface ITelemetryService
    {
        Task<TelemetryResult> ReceiveAsync(TelemetryPacketBase packet);
        Task<List<TelemetryPacketBase>> GetHistoryAsync(int deviceId, int? sensorId = null, int take = 100);
        Task<TelemetryPacketBase?> GetLatestAsync(int deviceId, int sensorId);
        Task<PowerReading> GetAggregatePowerAsync(int deviceId);
    }

    public class TelemetryService : ITelemetryService
    {
        private readonly SmartXDbContext _context;
        private readonly ITelemetryValidationService _validationService;
        private readonly ITelemetryProcessingService _processingService;
        private readonly ITelemetryStreamProcessor _stream;

        public TelemetryService(
            SmartXDbContext context,
            ITelemetryValidationService validationService,
            ITelemetryProcessingService processingService,
            ITelemetryStreamProcessor stream)
        {
            _context = context;
            _validationService = validationService;
            _processingService = processingService;
            _stream = stream;
        }

        public async Task<TelemetryResult> ReceiveAsync(TelemetryPacketBase packet)
        {
            var validation = await _validationService.ValidateAsync(packet);
            if (!validation.IsValid)
            {
                return validation;
            }

            var processed = _processingService.Process(packet);

            var record = TelemetryRecord.FromPacket(packet);
            _context.TelemetryRecords.Add(record);
            await _context.SaveChangesAsync();

            if (!_stream.Ingest(packet))
            {
                await _stream.ReloadDeviceAsync(packet.DeviceId);
                _stream.Ingest(packet);
            }

            return processed;
        }

        public async Task<List<TelemetryPacketBase>> GetHistoryAsync(int deviceId, int? sensorId = null, int take = 100)
        {
            var query = _context.TelemetryRecords.AsNoTracking().Where(t => t.DeviceId == deviceId);
            if (sensorId.HasValue)
            {
                query = query.Where(t => t.SensorId == sensorId.Value);
            }

            var records = await query
                .OrderByDescending(t => t.Timestamp)
                .Take(take)
                .ToListAsync();

            var packets = new List<TelemetryPacketBase>();
            foreach (var record in records)
            {
                packets.Add(record.ToPacket());
            }

            return packets;
        }

        public async Task<TelemetryPacketBase?> GetLatestAsync(int deviceId, int sensorId)
        {
            var record = await _context.TelemetryRecords
                .AsNoTracking()
                .Where(t => t.DeviceId == deviceId && t.SensorId == sensorId)
                .OrderByDescending(t => t.Timestamp)
                .FirstOrDefaultAsync();

            return record?.ToPacket();
        }

        public async Task<PowerReading> GetAggregatePowerAsync(int deviceId)
        {
            var powerSensorIds = await _context.Sensors
                .AsNoTracking()
                .Where(s => s.DeviceId == deviceId && s.Category == SensorCategory.PowerConsumption)
                .Select(s => s.Id)
                .ToListAsync();

            var readings = new List<PowerReading>();
            foreach (var sensorId in powerSensorIds)
            {
                var latest = await GetLatestAsync(deviceId, sensorId);
                if (latest is TelemetryPacket<int> intPacket)
                {
                    readings.Add(new PowerReading(intPacket.Value));
                }
                else if (latest is TelemetryPacket<float> floatPacket)
                {
                    readings.Add(new PowerReading(floatPacket.Value));
                }
            }

            return _processingService.AggregatePower(readings);
        }
    }
}
