using SmartXApi.Data;
using SmartXApi.Models;

namespace SmartXApi.Services
{
    public interface ISensorService
    {
        Task<List<Sensor>> GetAllAsync();
        Task<List<Sensor>> GetByDeviceIdAsync(int deviceId);
        Task<Sensor?> GetByIdAsync(int id);
        Task<Sensor?> CreateAsync(Sensor sensor);
        Task<bool> UpdateAsync(int id, Sensor sensor);
        Task<bool> DeleteAsync(int id);
    }

    public class SensorService : ISensorService
    {
        private readonly IDataStore _store;
        private readonly ITelemetryStreamProcessor _stream;

        public SensorService(IDataStore store, ITelemetryStreamProcessor stream)
        {
            _store = store;
            _stream = stream;
        }

        public Task<List<Sensor>> GetAllAsync() => Task.FromResult(_store.GetSensors());

        public Task<List<Sensor>> GetByDeviceIdAsync(int deviceId) => Task.FromResult(_store.GetSensors(deviceId));

        public Task<Sensor?> GetByIdAsync(int id) => Task.FromResult(_store.GetSensor(id));

        public async Task<Sensor?> CreateAsync(Sensor sensor)
        {
            var created = _store.AddSensor(sensor);
            if (created is null)
            {
                return null;
            }

            await _stream.ReloadDeviceAsync(created.DeviceId);
            return created;
        }

        public async Task<bool> UpdateAsync(int id, Sensor sensor)
        {
            var updated = _store.UpdateSensor(id, existing =>
            {
                existing.Name = sensor.Name;
                existing.Category = sensor.Category;
                existing.ValueType = sensor.ValueType;
                existing.Unit = sensor.Unit;
            });

            if (!updated)
            {
                return false;
            }

            var deviceId = _store.GetSensor(id)?.DeviceId;
            if (deviceId.HasValue)
            {
                await _stream.ReloadDeviceAsync(deviceId.Value);
            }

            return true;
        }

        public async Task<bool> DeleteAsync(int id)
        {
            var removed = _store.RemoveSensor(id);
            if (removed is null)
            {
                return false;
            }

            await _stream.ReloadDeviceAsync(removed.DeviceId);
            return true;
        }
    }
}
