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
        private readonly IDataStore _store;
        private readonly ITelemetryValidationService _validationService;
        private readonly ITelemetryProcessingService _processingService;
        private readonly ITelemetryStreamProcessor _stream;

        public TelemetryService(
            IDataStore store,
            ITelemetryValidationService validationService,
            ITelemetryProcessingService processingService,
            ITelemetryStreamProcessor stream)
        {
            _store = store;
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
            _store.AddTelemetry(record);

            if (!_stream.Ingest(packet))
            {
                await _stream.ReloadDeviceAsync(packet.DeviceId);
                _stream.Ingest(packet);
            }

            return processed;
        }

        public Task<List<TelemetryPacketBase>> GetHistoryAsync(int deviceId, int? sensorId = null, int take = 100)
        {
            var packets = _store.GetTelemetry(deviceId, sensorId, take)
                .Select(record => record.ToPacket())
                .ToList();

            return Task.FromResult(packets);
        }

        public Task<TelemetryPacketBase?> GetLatestAsync(int deviceId, int sensorId)
        {
            var record = _store.GetLatestTelemetry(deviceId, sensorId);
            return Task.FromResult(record?.ToPacket());
        }

        public async Task<PowerReading> GetAggregatePowerAsync(int deviceId)
        {
            var powerSensorIds = _store.GetSensors(deviceId)
                .Where(s => s.Category == SensorCategory.PowerConsumption)
                .Select(s => s.Id)
                .ToList();

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
