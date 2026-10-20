using System.Text.RegularExpressions;
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

        private readonly IDataStore _store;
        private readonly ITelemetryStreamProcessor _stream;

        public DeviceService(IDataStore store, ITelemetryStreamProcessor stream)
        {
            _store = store;
            _stream = stream;
        }

        public Task<List<Device>> GetAllAsync() => Task.FromResult(_store.GetDevices());

        public Task<Device?> GetByIdAsync(int id) => Task.FromResult(_store.GetDevice(id));

        public Task<string?> ValidateAsync(Device device, int? exceptId = null) =>
            Task.FromResult(Validate(device, exceptId));

        private string? Validate(Device device, int? exceptId)
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
            if (_store.IdentifierTaken(identifier, exceptId))
            {
                return $"A device with identifier '{identifier}' already exists.";
            }

            if (mac.Length > 0)
            {
                var candidates = _store.GetMacAddresses(exceptId);
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
            var created = _store.AddDevice(device);
            await _stream.ReloadDeviceAsync(created.Id);
            return created;
        }

        public async Task<bool> UpdateAsync(int id, Device device)
        {
            var updated = _store.UpdateDevice(id, existing =>
            {
                existing.DeviceIdentifier = device.DeviceIdentifier;
                existing.MacAddress = TelemetryStreamProcessor.NormalizeMac(device.MacAddress);
                existing.Location = device.Location;
                existing.Room = device.Room;
                existing.NodeId = device.NodeId;
                existing.IsOnline = device.IsOnline;
            });

            if (!updated)
            {
                return false;
            }

            await _stream.ReloadDeviceAsync(id);
            return true;
        }

        public Task<bool> DeleteAsync(int id)
        {
            if (!_store.RemoveDevice(id))
            {
                return Task.FromResult(false);
            }

            _stream.RemoveDevice(id);
            return Task.FromResult(true);
        }
    }
}
