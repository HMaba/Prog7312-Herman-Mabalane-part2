using Microsoft.EntityFrameworkCore;
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
        private readonly SmartXDbContext _context;
        private readonly ITelemetryStreamProcessor _stream;

        public SensorService(SmartXDbContext context, ITelemetryStreamProcessor stream)
        {
            _context = context;
            _stream = stream;
        }

        public async Task<List<Sensor>> GetAllAsync() =>
            await _context.Sensors.AsNoTracking().ToListAsync();

        public async Task<List<Sensor>> GetByDeviceIdAsync(int deviceId) =>
            await _context.Sensors.AsNoTracking().Where(s => s.DeviceId == deviceId).ToListAsync();

        public async Task<Sensor?> GetByIdAsync(int id) =>
            await _context.Sensors.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id);

        public async Task<Sensor?> CreateAsync(Sensor sensor)
        {
            var deviceExists = await _context.Devices.AnyAsync(d => d.Id == sensor.DeviceId);
            if (!deviceExists)
            {
                return null;
            }

            _context.Sensors.Add(sensor);
            await _context.SaveChangesAsync();
            await _stream.ReloadDeviceAsync(sensor.DeviceId);
            return sensor;
        }

        public async Task<bool> UpdateAsync(int id, Sensor sensor)
        {
            var existing = await _context.Sensors.FirstOrDefaultAsync(s => s.Id == id);
            if (existing is null)
            {
                return false;
            }

            existing.Name = sensor.Name;
            existing.Category = sensor.Category;
            existing.ValueType = sensor.ValueType;
            existing.Unit = sensor.Unit;

            await _context.SaveChangesAsync();
            await _stream.ReloadDeviceAsync(existing.DeviceId);
            return true;
        }

        public async Task<bool> DeleteAsync(int id)
        {
            var existing = await _context.Sensors.FirstOrDefaultAsync(s => s.Id == id);
            if (existing is null)
            {
                return false;
            }

            var deviceId = existing.DeviceId;
            _context.Sensors.Remove(existing);
            await _context.SaveChangesAsync();
            await _stream.ReloadDeviceAsync(deviceId);
            return true;
        }
    }
}
