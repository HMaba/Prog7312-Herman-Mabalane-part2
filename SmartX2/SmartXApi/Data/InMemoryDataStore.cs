using SmartXApi.Models;

namespace SmartXApi.Data
{
    /// <summary>
    /// Storage abstraction used by every service. The only implementation is
    /// <see cref="InMemoryDataStore"/>; there is no database behind it.
    /// All reads return copies, so callers can never mutate stored state by accident.
    /// </summary>
    public interface IDataStore
    {
        // Devices
        List<Device> GetDevices(bool includeChildren = true);
        Device? GetDevice(int id, bool includeChildren = true);
        bool DeviceExists(int id);
        bool IdentifierTaken(string identifier, int? exceptId = null);
        List<string> GetMacAddresses(int? exceptId = null);
        Device AddDevice(Device device);
        bool UpdateDevice(int id, Action<Device> apply);
        bool RemoveDevice(int id);

        // Sensors
        List<Sensor> GetSensors(int? deviceId = null);
        Sensor? GetSensor(int id);
        Sensor? AddSensor(Sensor sensor);
        bool UpdateSensor(int id, Action<Sensor> apply);
        Sensor? RemoveSensor(int id);

        // Attachments
        List<Attachment> GetAttachments(int deviceId);
        Attachment? GetAttachment(int id);
        byte[]? GetAttachmentContent(int id);
        Attachment? AddAttachment(Attachment attachment, byte[] content);
        bool RemoveAttachment(int id);

        // Telemetry
        TelemetryRecord AddTelemetry(TelemetryRecord record);
        List<TelemetryRecord> GetTelemetry(int deviceId, int? sensorId, int take);
        TelemetryRecord? GetLatestTelemetry(int deviceId, int sensorId);

        // Bulk device import (applied atomically)
        void ApplyImport(IEnumerable<Device> newDevices, IEnumerable<Device> updatedDevices);
    }

    public sealed class InMemoryDataStore : IDataStore
    {
        // Telemetry is the only collection that grows forever, so it is capped.
        private const int MaxTelemetryRecords = 50_000;

        private readonly object _gate = new();

        private readonly List<Device> _devices = new();
        private readonly List<Sensor> _sensors = new();
        private readonly List<Attachment> _attachments = new();
        private readonly Dictionary<int, byte[]> _attachmentContent = new();
        private readonly List<TelemetryRecord> _telemetry = new();

        private int _nextDeviceId = 1;
        private int _nextSensorId = 1;
        private int _nextAttachmentId = 1;
        private int _nextTelemetryId = 1;

        public InMemoryDataStore()
        {
            DataSeeder.Seed(this);
        }

        // ---------------------------------------------------------------- Devices

        public List<Device> GetDevices(bool includeChildren = true)
        {
            lock (_gate)
            {
                return _devices.Select(d => CloneDevice(d, includeChildren)).ToList();
            }
        }

        public Device? GetDevice(int id, bool includeChildren = true)
        {
            lock (_gate)
            {
                var device = _devices.FirstOrDefault(d => d.Id == id);
                return device is null ? null : CloneDevice(device, includeChildren);
            }
        }

        public bool DeviceExists(int id)
        {
            lock (_gate)
            {
                return _devices.Any(d => d.Id == id);
            }
        }

        public bool IdentifierTaken(string identifier, int? exceptId = null)
        {
            lock (_gate)
            {
                return _devices.Any(d =>
                    d.DeviceIdentifier == identifier && (!exceptId.HasValue || d.Id != exceptId.Value));
            }
        }

        public List<string> GetMacAddresses(int? exceptId = null)
        {
            lock (_gate)
            {
                return _devices
                    .Where(d => !exceptId.HasValue || d.Id != exceptId.Value)
                    .Select(d => d.MacAddress)
                    .ToList();
            }
        }

        public Device AddDevice(Device device)
        {
            lock (_gate)
            {
                var stored = AddDeviceCore(device);
                return CloneDevice(stored, true);
            }
        }

        public bool UpdateDevice(int id, Action<Device> apply)
        {
            lock (_gate)
            {
                var existing = _devices.FirstOrDefault(d => d.Id == id);
                if (existing is null)
                {
                    return false;
                }

                apply(existing);
                return true;
            }
        }

        public bool RemoveDevice(int id)
        {
            lock (_gate)
            {
                var existing = _devices.FirstOrDefault(d => d.Id == id);
                if (existing is null)
                {
                    return false;
                }

                // Cascade: sensors, attachments (and their content) and telemetry go with the device.
                _sensors.RemoveAll(s => s.DeviceId == id);

                foreach (var attachment in _attachments.Where(a => a.DeviceId == id).ToList())
                {
                    _attachmentContent.Remove(attachment.Id);
                    _attachments.Remove(attachment);
                }

                _telemetry.RemoveAll(t => t.DeviceId == id);
                _devices.Remove(existing);
                return true;
            }
        }

        // ---------------------------------------------------------------- Sensors

        public List<Sensor> GetSensors(int? deviceId = null)
        {
            lock (_gate)
            {
                return _sensors
                    .Where(s => !deviceId.HasValue || s.DeviceId == deviceId.Value)
                    .Select(CloneSensor)
                    .ToList();
            }
        }

        public Sensor? GetSensor(int id)
        {
            lock (_gate)
            {
                var sensor = _sensors.FirstOrDefault(s => s.Id == id);
                return sensor is null ? null : CloneSensor(sensor);
            }
        }

        public Sensor? AddSensor(Sensor sensor)
        {
            lock (_gate)
            {
                var device = _devices.FirstOrDefault(d => d.Id == sensor.DeviceId);
                if (device is null)
                {
                    return null;
                }

                var stored = AddSensorCore(device, sensor);
                return CloneSensor(stored);
            }
        }

        public bool UpdateSensor(int id, Action<Sensor> apply)
        {
            lock (_gate)
            {
                var existing = _sensors.FirstOrDefault(s => s.Id == id);
                if (existing is null)
                {
                    return false;
                }

                apply(existing);
                return true;
            }
        }

        public Sensor? RemoveSensor(int id)
        {
            lock (_gate)
            {
                var existing = _sensors.FirstOrDefault(s => s.Id == id);
                if (existing is null)
                {
                    return null;
                }

                _sensors.Remove(existing);
                _devices.FirstOrDefault(d => d.Id == existing.DeviceId)?.Sensors.Remove(existing);
                _telemetry.RemoveAll(t => t.SensorId == id);
                return CloneSensor(existing);
            }
        }

        // ------------------------------------------------------------ Attachments

        public List<Attachment> GetAttachments(int deviceId)
        {
            lock (_gate)
            {
                return _attachments.Where(a => a.DeviceId == deviceId).Select(CloneAttachment).ToList();
            }
        }

        public Attachment? GetAttachment(int id)
        {
            lock (_gate)
            {
                var attachment = _attachments.FirstOrDefault(a => a.Id == id);
                return attachment is null ? null : CloneAttachment(attachment);
            }
        }

        public byte[]? GetAttachmentContent(int id)
        {
            lock (_gate)
            {
                return _attachmentContent.TryGetValue(id, out var bytes) ? (byte[])bytes.Clone() : null;
            }
        }

        public Attachment? AddAttachment(Attachment attachment, byte[] content)
        {
            lock (_gate)
            {
                var device = _devices.FirstOrDefault(d => d.Id == attachment.DeviceId);
                if (device is null)
                {
                    return null;
                }

                var stored = AddAttachmentCore(device, attachment, content);
                return CloneAttachment(stored);
            }
        }

        public bool RemoveAttachment(int id)
        {
            lock (_gate)
            {
                var existing = _attachments.FirstOrDefault(a => a.Id == id);
                if (existing is null)
                {
                    return false;
                }

                _attachments.Remove(existing);
                _attachmentContent.Remove(id);
                _devices.FirstOrDefault(d => d.Id == existing.DeviceId)?.Attachments.Remove(existing);
                return true;
            }
        }

        // -------------------------------------------------------------- Telemetry

        public TelemetryRecord AddTelemetry(TelemetryRecord record)
        {
            lock (_gate)
            {
                var stored = AddTelemetryCore(record);
                return CloneTelemetry(stored);
            }
        }

        public List<TelemetryRecord> GetTelemetry(int deviceId, int? sensorId, int take)
        {
            lock (_gate)
            {
                // _telemetry is append-ordered; sort by timestamp (then id) newest first, like the old query.
                return _telemetry
                    .Where(t => t.DeviceId == deviceId && (!sensorId.HasValue || t.SensorId == sensorId.Value))
                    .OrderByDescending(t => t.Timestamp)
                    .ThenByDescending(t => t.Id)
                    .Take(Math.Max(0, take))
                    .Select(CloneTelemetry)
                    .ToList();
            }
        }

        public TelemetryRecord? GetLatestTelemetry(int deviceId, int sensorId)
        {
            lock (_gate)
            {
                var latest = _telemetry
                    .Where(t => t.DeviceId == deviceId && t.SensorId == sensorId)
                    .OrderByDescending(t => t.Timestamp)
                    .ThenByDescending(t => t.Id)
                    .FirstOrDefault();

                return latest is null ? null : CloneTelemetry(latest);
            }
        }

        // ----------------------------------------------------------------- Import

        public void ApplyImport(IEnumerable<Device> newDevices, IEnumerable<Device> updatedDevices)
        {
            lock (_gate)
            {
                foreach (var updated in updatedDevices)
                {
                    var existing = _devices.FirstOrDefault(d => d.Id == updated.Id);
                    if (existing is null)
                    {
                        continue;
                    }

                    existing.DeviceIdentifier = updated.DeviceIdentifier;
                    existing.Location = updated.Location;
                    existing.Room = updated.Room;
                    existing.NodeId = updated.NodeId;

                    foreach (var sensor in updated.Sensors.Where(s => s.Id == 0))
                    {
                        sensor.DeviceId = existing.Id;
                        AddSensorCore(existing, sensor);
                    }
                }

                foreach (var device in newDevices)
                {
                    AddDeviceCore(device);
                }
            }
        }

        // ---------------------------------------------- Internals (call under _gate)

        private Device AddDeviceCore(Device source)
        {
            var device = new Device
            {
                Id = _nextDeviceId++,
                DeviceIdentifier = source.DeviceIdentifier,
                MacAddress = source.MacAddress,
                Location = source.Location,
                Room = source.Room,
                NodeId = source.NodeId,
                IsOnline = source.IsOnline,
                RegisteredAt = source.RegisteredAt
            };

            _devices.Add(device);

            foreach (var sensor in source.Sensors)
            {
                AddSensorCore(device, sensor);
            }

            foreach (var attachment in source.Attachments)
            {
                AddAttachmentCore(device, attachment, Array.Empty<byte>());
            }

            return device;
        }

        private Sensor AddSensorCore(Device device, Sensor source)
        {
            var sensor = new Sensor
            {
                Id = _nextSensorId++,
                DeviceId = device.Id,
                Name = source.Name,
                Category = source.Category,
                ValueType = source.ValueType,
                Unit = source.Unit
            };

            _sensors.Add(sensor);
            device.Sensors.Add(sensor);
            return sensor;
        }

        private Attachment AddAttachmentCore(Device device, Attachment source, byte[] content)
        {
            var id = _nextAttachmentId++;
            var attachment = new Attachment
            {
                Id = id,
                DeviceId = device.Id,
                FileName = source.FileName,
                FileType = source.FileType,
                FilePath = $"memory://attachments/{id}",
                FileSizeBytes = content.LongLength,
                UploadedAt = source.UploadedAt
            };

            _attachments.Add(attachment);
            _attachmentContent[id] = content;
            device.Attachments.Add(attachment);
            return attachment;
        }

        private TelemetryRecord AddTelemetryCore(TelemetryRecord source)
        {
            var record = new TelemetryRecord
            {
                Id = _nextTelemetryId++,
                DeviceId = source.DeviceId,
                SensorId = source.SensorId,
                Timestamp = source.Timestamp,
                ValueType = source.ValueType,
                ValueJson = source.ValueJson
            };

            _telemetry.Add(record);

            if (_telemetry.Count > MaxTelemetryRecords)
            {
                _telemetry.RemoveRange(0, _telemetry.Count - MaxTelemetryRecords);
            }

            return record;
        }

        private static Device CloneDevice(Device d, bool includeChildren)
        {
            var copy = new Device
            {
                Id = d.Id,
                DeviceIdentifier = d.DeviceIdentifier,
                MacAddress = d.MacAddress,
                Location = d.Location,
                Room = d.Room,
                NodeId = d.NodeId,
                IsOnline = d.IsOnline,
                RegisteredAt = d.RegisteredAt
            };

            if (includeChildren)
            {
                copy.Sensors = d.Sensors.Select(CloneSensor).ToList();
                copy.Attachments = d.Attachments.Select(CloneAttachment).ToList();
            }

            return copy;
        }

        private static Sensor CloneSensor(Sensor s) => new()
        {
            Id = s.Id,
            DeviceId = s.DeviceId,
            Name = s.Name,
            Category = s.Category,
            ValueType = s.ValueType,
            Unit = s.Unit
        };

        private static Attachment CloneAttachment(Attachment a) => new()
        {
            Id = a.Id,
            DeviceId = a.DeviceId,
            FileName = a.FileName,
            FileType = a.FileType,
            FilePath = a.FilePath,
            FileSizeBytes = a.FileSizeBytes,
            UploadedAt = a.UploadedAt
        };

        private static TelemetryRecord CloneTelemetry(TelemetryRecord t) => new()
        {
            Id = t.Id,
            DeviceId = t.DeviceId,
            SensorId = t.SensorId,
            Timestamp = t.Timestamp,
            ValueType = t.ValueType,
            ValueJson = t.ValueJson
        };
    }
}
