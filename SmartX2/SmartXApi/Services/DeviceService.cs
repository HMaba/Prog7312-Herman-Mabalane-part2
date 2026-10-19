using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using SmartXApi.Data;
using SmartXApi.Models;

namespace SmartXApi.Services
{
    public interface IDeviceService
    {
        Task<List<Device>> GetAllAsync();
        Task<Device?> GetByIdAsync(int id);
        Task<string?> ValidateAsync(Device device, int? exceptId = null);
        Task<Device> CreateAsync(Device device);
        Task<bool> UpdateAsync(int id, Device device);
        Task<bool> DeleteAsync(int id);
    }

    public class DeviceService : IDeviceService
    {
        private static readonly Regex MacPattern = new("^([0-9A-F]{2}:){5}[0-9A-F]{2}$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private readonly SmartXDbContext _context;
        private readonly ITelemetryStreamProcessor _stream;

        public DeviceService(SmartXDbContext context, ITelemetryStreamProcessor stream)
        {
            _context = context;
            _stream = stream;
        }

        public async Task<List<Device>> GetAllAsync() =>
            await _context.Devices
                .Include(d => d.Sensors)
                .Include(d => d.Attachments)
                .AsNoTracking()
                .ToListAsync();

        public async Task<Device?> GetByIdAsync(int id) =>
            await _context.Devices
                .Include(d => d.Sensors)
                .Include(d => d.Attachments)
                .AsNoTracking()
                .FirstOrDefaultAsync(d => d.Id == id);

        public async Task<string?> ValidateAsync(Device device, int? exceptId = null)
        {
            if (string.IsNullOrWhiteSpace(device.DeviceIdentifier) || device.DeviceIdentifier.Length > 50)
            {
                return "DeviceIdentifier is required and cannot exceed 50 characters.";
            }

            var mac = TelemetryStreamProcessor.NormalizeMac(device.MacAddress);
            if (mac.Length > 0 && !MacPattern.IsMatch(mac))
            {
                return $"'{device.MacAddress}' is not a valid MAC address.";
            }

            var identifier = device.DeviceIdentifier.Trim();
            var identifierTaken = await _context.Devices.AnyAsync(d =>
                d.DeviceIdentifier == identifier && (!exceptId.HasValue || d.Id != exceptId.Value));
            if (identifierTaken)
            {
                return $"A device with identifier '{identifier}' already exists.";
            }

            if (mac.Length > 0)
            {
                var candidates = await _context.Devices
                    .AsNoTracking()
                    .Where(d => !exceptId.HasValue || d.Id != exceptId.Value)
                    .Select(d => d.MacAddress)
                    .ToListAsync();

                if (candidates.Any(m => TelemetryStreamProcessor.NormalizeMac(m) == mac))
                {
                    return $"A device with MAC address '{mac}' already exists.";
                }
            }

            return null;
        }

        public async Task<Device> CreateAsync(Device device)
        {
            device.RegisteredAt = DateTime.UtcNow;
            device.MacAddress = TelemetryStreamProcessor.NormalizeMac(device.MacAddress);
            _context.Devices.Add(device);
            await _context.SaveChangesAsync();
            await _stream.ReloadDeviceAsync(device.Id);
            return device;
        }

        public async Task<bool> UpdateAsync(int id, Device device)
        {
            var existing = await _context.Devices.FirstOrDefaultAsync(d => d.Id == id);
            if (existing is null)
            {
                return false;
            }

            existing.DeviceIdentifier = device.DeviceIdentifier;
            existing.MacAddress = TelemetryStreamProcessor.NormalizeMac(device.MacAddress);
            existing.Location = device.Location;
            existing.Room = device.Room;
            existing.NodeId = device.NodeId;
            existing.IsOnline = device.IsOnline;

            await _context.SaveChangesAsync();
            await _stream.ReloadDeviceAsync(id);
            return true;
        }

        public async Task<bool> DeleteAsync(int id)
        {
            var existing = await _context.Devices.FirstOrDefaultAsync(d => d.Id == id);
            if (existing is null)
            {
                return false;
            }

            _context.Devices.Remove(existing);
            await _context.SaveChangesAsync();
            _stream.RemoveDevice(id);
            return true;
        }
    }
}
