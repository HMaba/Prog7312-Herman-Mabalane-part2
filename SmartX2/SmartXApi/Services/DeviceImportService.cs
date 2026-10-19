using Microsoft.EntityFrameworkCore;
using SmartXApi.Data;
using SmartXApi.Models;
using System.Text;
using System.Text.RegularExpressions;

namespace SmartXApi.Services
{
    public interface IDeviceImportService
    {
        Task<DeviceImportResult> ImportAsync(string fileName, Stream content);
    }

    public class DeviceImportService : IDeviceImportService
    {
        private const int MaxLines = 5000;
        private const int MaxFieldLength = 100;

        private static readonly Regex MacPattern = new("^([0-9A-F]{2}:){5}[0-9A-F]{2}$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex IdentifierPattern = new("^[A-Za-z0-9_.\\-]{1,50}$", RegexOptions.Compiled);

        private readonly SmartXDbContext _context;
        private readonly ITelemetryStreamProcessor _processor;
        private readonly ILogger<DeviceImportService> _logger;

        public DeviceImportService(SmartXDbContext context, ITelemetryStreamProcessor processor, ILogger<DeviceImportService> logger)
        {
            _context = context;
            _processor = processor;
            _logger = logger;
        }

        public async Task<DeviceImportResult> ImportAsync(string fileName, Stream content)
        {
            var result = new DeviceImportResult { FileName = Path.GetFileName(fileName) };
            var parsed = new List<ParsedDevice>();
            var seenMacs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var seenIdentifiers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            using (var reader = new StreamReader(content, Encoding.UTF8, true))
            {
                var lineNumber = 0;
                string? line;

                while ((line = await reader.ReadLineAsync()) is not null)
                {
                    lineNumber++;

                    if (lineNumber > MaxLines)
                    {
                        result.Errors.Add(new ImportLineError { Line = lineNumber, Message = $"File exceeds {MaxLines} lines; remaining lines were ignored." });
                        break;
                    }

                    var trimmed = line.Trim().TrimStart('\uFEFF');
                    if (trimmed.Length == 0 || trimmed.StartsWith('#') || trimmed.StartsWith("//"))
                    {
                        continue;
                    }

                    result.LinesRead++;

                    if (IsHeader(trimmed))
                    {
                        continue;
                    }

                    if (!TryParseLine(trimmed, out var device, out var error))
                    {
                        result.Skipped++;
                        result.Errors.Add(new ImportLineError { Line = lineNumber, Message = error });
                        continue;
                    }

                    if (!seenMacs.Add(device.Mac))
                    {
                        result.Skipped++;
                        result.Errors.Add(new ImportLineError { Line = lineNumber, Message = $"MAC address {device.Mac} appears more than once in the file." });
                        continue;
                    }

                    if (!seenIdentifiers.Add(device.Identifier))
                    {
                        result.Skipped++;
                        result.Errors.Add(new ImportLineError { Line = lineNumber, Message = $"Device identifier {device.Identifier} appears more than once in the file." });
                        continue;
                    }

                    device.Line = lineNumber;
                    parsed.Add(device);
                }
            }

            if (parsed.Count == 0)
            {
                return result;
            }

            var existing = await _context.Devices.Include(d => d.Sensors).ToListAsync();
            var byMac = new Dictionary<string, Device>(StringComparer.OrdinalIgnoreCase);
            var byIdentifier = new Dictionary<string, Device>(StringComparer.OrdinalIgnoreCase);

            foreach (var device in existing)
            {
                var mac = TelemetryStreamProcessor.NormalizeMac(device.MacAddress);
                if (mac.Length > 0)
                {
                    byMac.TryAdd(mac, device);
                }

                byIdentifier.TryAdd(device.DeviceIdentifier, device);
            }

            var touched = new List<Device>();
            var created = 0;
            var updated = 0;

            foreach (var item in parsed)
            {
                if (byMac.TryGetValue(item.Mac, out var current))
                {
                    if (!string.Equals(current.DeviceIdentifier, item.Identifier, StringComparison.OrdinalIgnoreCase))
                    {
                        if (byIdentifier.TryGetValue(item.Identifier, out var holder) && !ReferenceEquals(holder, current))
                        {
                            result.Skipped++;
                            result.Errors.Add(new ImportLineError { Line = item.Line, Message = $"Identifier {item.Identifier} already belongs to a different device." });
                            continue;
                        }

                        byIdentifier.Remove(current.DeviceIdentifier);
                        current.DeviceIdentifier = item.Identifier;
                        byIdentifier[item.Identifier] = current;
                    }

                    current.Location = item.Location;
                    current.Room = item.Room;
                    current.NodeId = item.NodeId;

                    foreach (var template in item.Sensors)
                    {
                        if (!current.Sensors.Any(s => string.Equals(s.Name, template.Name, StringComparison.OrdinalIgnoreCase)))
                        {
                            current.Sensors.Add(template.ToSensor());
                        }
                    }

                    updated++;
                    touched.Add(current);
                    continue;
                }

                if (byIdentifier.ContainsKey(item.Identifier))
                {
                    result.Skipped++;
                    result.Errors.Add(new ImportLineError { Line = item.Line, Message = $"Identifier {item.Identifier} is already registered to a different MAC address." });
                    continue;
                }

                var device = new Device
                {
                    DeviceIdentifier = item.Identifier,
                    MacAddress = item.Mac,
                    Location = item.Location,
                    Room = item.Room,
                    NodeId = item.NodeId,
                    IsOnline = false,
                    RegisteredAt = DateTime.UtcNow,
                    Sensors = item.Sensors.Select(t => t.ToSensor()).ToList()
                };

                _context.Devices.Add(device);
                byMac[item.Mac] = device;
                byIdentifier[item.Identifier] = device;
                created++;
                touched.Add(device);
            }

            if (touched.Count == 0)
            {
                return result;
            }

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException ex)
            {
                _logger.LogError(ex, "Device import could not be saved.");
                result.Skipped += created + updated;
                result.Errors.Add(new ImportLineError { Line = 0, Message = "The database rejected the import; no devices were changed." });
                return result;
            }

            result.Created = created;
            result.Updated = updated;

            await _processor.ReloadAllAsync();
            result.Devices = _processor.GetDevices(touched.Select(d => d.Id).Distinct());
            return result;
        }

        private static bool IsHeader(string line)
        {
            var lower = line.ToLowerInvariant();
            return lower.StartsWith("device") && lower.Contains("mac");
        }

        private static bool TryParseLine(string line, out ParsedDevice device, out string error)
        {
            device = new ParsedDevice();
            error = string.Empty;

            var parts = line.Split(new[] { ',', ';', '\t' }).Select(p => p.Trim()).ToArray();
            if (parts.Length < 2)
            {
                error = "Expected at least DeviceIdentifier and MacAddress separated by commas.";
                return false;
            }

            var identifier = parts[0];
            if (!IdentifierPattern.IsMatch(identifier))
            {
                error = $"'{Clean(identifier)}' is not a valid device identifier (letters, digits, '-', '_' and '.', max 50).";
                return false;
            }

            var mac = TelemetryStreamProcessor.NormalizeMac(parts[1]);
            if (!MacPattern.IsMatch(mac))
            {
                error = $"'{Clean(parts[1])}' is not a valid MAC address.";
                return false;
            }

            var location = Clean(Get(parts, 2));
            var room = Clean(Get(parts, 3));
            var nodeId = Clean(Get(parts, 4));
            if (nodeId.Length == 0)
            {
                nodeId = $"NODE-{identifier}";
            }

            var spec = Get(parts, 5);
            List<SensorTemplate> sensors;

            if (spec.Contains(':'))
            {
                if (!TryParseExplicitSensors(spec, out sensors, out error))
                {
                    return false;
                }
            }
            else
            {
                sensors = ResolveTemplates(spec, identifier, location, room);
            }

            device = new ParsedDevice
            {
                Identifier = identifier,
                Mac = mac,
                Location = location,
                Room = room,
                NodeId = nodeId,
                Sensors = sensors
            };

            return true;
        }

        private static bool TryParseExplicitSensors(string spec, out List<SensorTemplate> sensors, out string error)
        {
            sensors = new List<SensorTemplate>();
            error = string.Empty;

            foreach (var entry in spec.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var fields = entry.Split(':').Select(f => f.Trim()).ToArray();
                if (fields.Length < 3)
                {
                    error = $"Sensor '{Clean(entry)}' must look like Name:Category:ValueType:Unit.";
                    return false;
                }

                var name = Clean(fields[0]);
                if (name.Length == 0)
                {
                    error = "Sensor name cannot be empty.";
                    return false;
                }

                if (!Enum.TryParse<SensorCategory>(fields[1], true, out var category) || !Enum.IsDefined(category))
                {
                    error = $"'{Clean(fields[1])}' is not a valid sensor category (Environmental, PowerConsumption, Actuator).";
                    return false;
                }

                if (!Enum.TryParse<TelemetryValueType>(fields[2], true, out var valueType) || !Enum.IsDefined(valueType))
                {
                    error = $"'{Clean(fields[2])}' is not a valid value type (Float, Int, Bool, String).";
                    return false;
                }

                sensors.Add(new SensorTemplate(name, category, valueType, Clean(Get(fields, 3))));
            }

            if (sensors.Count == 0)
            {
                error = "No sensors were found in the sensor specification.";
                return false;
            }

            return true;
        }

        private static List<SensorTemplate> ResolveTemplates(string keyword, string identifier, string location, string room)
        {
            var source = string.IsNullOrWhiteSpace(keyword) ? $"{identifier} {location} {room}" : keyword;
            var key = source.ToLowerInvariant();

            var moisture = new SensorTemplate("Soil Moisture", SensorCategory.Environmental, TelemetryValueType.Float, "%");
            var temperature = new SensorTemplate("Ambient Temperature", SensorCategory.Environmental, TelemetryValueType.Float, "°C");
            var humidity = new SensorTemplate("Humidity", SensorCategory.Environmental, TelemetryValueType.Float, "%");
            var power = new SensorTemplate("Power Meter", SensorCategory.PowerConsumption, TelemetryValueType.Int, "W");

            if (key.Contains("plug"))
            {
                return new List<SensorTemplate> { power, new SensorTemplate("Smart Relay", SensorCategory.Actuator, TelemetryValueType.Bool, "on/off") };
            }

            if (key.Contains("pump"))
            {
                return new List<SensorTemplate> { new SensorTemplate("Water Pump", SensorCategory.Actuator, TelemetryValueType.Bool, "on/off") };
            }

            if (key.Contains("valve"))
            {
                return new List<SensorTemplate> { new SensorTemplate("Irrigation Valve", SensorCategory.Actuator, TelemetryValueType.Bool, "on/off") };
            }

            if (key.Contains("relay"))
            {
                return new List<SensorTemplate> { new SensorTemplate("Smart Relay", SensorCategory.Actuator, TelemetryValueType.Bool, "on/off") };
            }

            if (key.Contains("power") || key.Contains("meter"))
            {
                return new List<SensorTemplate> { power };
            }

            if (key.Contains("humid"))
            {
                return new List<SensorTemplate> { humidity, temperature };
            }

            if (key.Contains("moisture") || key.Contains("soil"))
            {
                return new List<SensorTemplate> { moisture, temperature };
            }

            if (key.Contains("temp"))
            {
                return new List<SensorTemplate> { temperature };
            }

            return new List<SensorTemplate> { moisture, temperature };
        }

        private static string Get(string[] parts, int index) => index < parts.Length ? parts[index] : string.Empty;

        private static string Clean(string value)
        {
            var builder = new StringBuilder(value.Length);
            foreach (var ch in value)
            {
                if (!char.IsControl(ch) && ch != '<' && ch != '>')
                {
                    builder.Append(ch);
                }
            }

            var cleaned = builder.ToString().Trim();
            return cleaned.Length <= MaxFieldLength ? cleaned : cleaned[..MaxFieldLength];
        }

        private sealed record SensorTemplate(string Name, SensorCategory Category, TelemetryValueType ValueType, string Unit)
        {
            public Sensor ToSensor() => new() { Name = Name, Category = Category, ValueType = ValueType, Unit = Unit };
        }

        private sealed class ParsedDevice
        {
            public int Line { get; set; }
            public string Identifier { get; set; } = string.Empty;
            public string Mac { get; set; } = string.Empty;
            public string Location { get; set; } = string.Empty;
            public string Room { get; set; } = string.Empty;
            public string NodeId { get; set; } = string.Empty;
            public List<SensorTemplate> Sensors { get; set; } = new();
        }
    }
}
