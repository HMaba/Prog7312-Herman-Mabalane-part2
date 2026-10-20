using SmartXApi.Data;
using SmartXApi.Models;
using System.Globalization;

namespace SmartXApi.Services
{
    
    public sealed class TelemetrySimulator : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IDataStore _store;
        private readonly IConfiguration _configuration;
        private readonly ILogger<TelemetrySimulator> _logger;

        private readonly Random _random = new();
        private readonly Dictionary<int, double> _values = new();
        private readonly Dictionary<int, int> _spikeTicks = new();
        private readonly HashSet<int> _publishedStates = new();
        private readonly DateTime _startedAt = DateTime.UtcNow;

        public TelemetrySimulator(
            IServiceScopeFactory scopeFactory,
            IDataStore store,
            IConfiguration configuration,
            ILogger<TelemetrySimulator> logger)
        {
            _scopeFactory = scopeFactory;
            _store = store;
            _configuration = configuration;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_configuration.GetValue("Simulator:Enabled", true))
            {
                _logger.LogInformation("Telemetry simulator is disabled (Simulator:Enabled = false).");
                return;
            }

            var seconds = Math.Max(1, _configuration.GetValue("Simulator:IntervalSeconds", 3));
            _logger.LogInformation("Telemetry simulator running: one reading per sensor every {Seconds}s.", seconds);

            try
            {
                // Give the stream worker a moment to load its device registry first.
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);

                using var timer = new PeriodicTimer(TimeSpan.FromSeconds(seconds));
                do
                {
                    try
                    {
                        await PublishTickAsync(stoppingToken);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _logger.LogWarning(ex, "Telemetry simulator cycle failed.");
                    }
                }
                while (await timer.WaitForNextTickAsync(stoppingToken));
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown.
            }
        }

        private async Task PublishTickAsync(CancellationToken cancellationToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var telemetry = scope.ServiceProvider.GetRequiredService<ITelemetryService>();

            var devices = _store.GetDevices();
            var elapsed = (DateTime.UtcNow - _startedAt).TotalSeconds;
            var irrigating = devices.Any(d => d.Sensors.Any(s =>
                s.ValueType == TelemetryValueType.Bool && IsWaterActuator(s) && IsOn(d.Id, s.Id)));

            foreach (var device in devices)
            {
                var hasMeasurements = device.Sensors.Any(s =>
                    s.ValueType == TelemetryValueType.Float || s.ValueType == TelemetryValueType.Int);
                var actuatorOn = device.Sensors.Any(s =>
                    s.ValueType == TelemetryValueType.Bool && IsOn(device.Id, s.Id));

                foreach (var sensor in device.Sensors)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var packet = BuildPacket(device, sensor, elapsed, irrigating, actuatorOn, hasMeasurements);
                    if (packet is null)
                    {
                        continue;
                    }

                    var result = await telemetry.ReceiveAsync(packet);
                    if (!result.IsValid)
                    {
                        _logger.LogDebug("Simulated reading for sensor {SensorId} rejected: {Message}", sensor.Id, result.Message);
                    }
                }
            }
        }

        private TelemetryPacketBase? BuildPacket(
            Device device, Sensor sensor, double elapsed, bool irrigating, bool actuatorOn, bool hasMeasurements)
        {
            var now = DateTime.UtcNow;
            var name = sensor.Name.ToLowerInvariant();

            switch (sensor.ValueType)
            {
                case TelemetryValueType.Float:
                    return new TelemetryPacket<float>
                    {
                        DeviceId = device.Id,
                        SensorId = sensor.Id,
                        Timestamp = now,
                        Value = (float)Math.Round(NextFloat(device.Id, sensor, name, elapsed, irrigating), 1)
                    };

                case TelemetryValueType.Int:
                    return new TelemetryPacket<int>
                    {
                        DeviceId = device.Id,
                        SensorId = sensor.Id,
                        Timestamp = now,
                        Value = NextInt(sensor, name, actuatorOn)
                    };

                case TelemetryValueType.Bool:
                    // Report the current state once so the dashboard shows it, then stay quiet unless this
                    // device has nothing else reporting (otherwise it would be flagged as disconnected).
                    if (_publishedStates.Add(sensor.Id) || !hasMeasurements)
                    {
                        return new TelemetryPacket<bool>
                        {
                            DeviceId = device.Id,
                            SensorId = sensor.Id,
                            Timestamp = now,
                            Value = IsOn(device.Id, sensor.Id)
                        };
                    }

                    return null;

                default:
                    return null;
            }
        }

        private double NextFloat(int deviceId, Sensor sensor, string name, double elapsed, bool irrigating)
        {
            if (name.Contains("moisture"))
            {
                var value = Current(deviceId, sensor.Id, 45);
                value += -0.15 + Noise(0.3);
                if (irrigating)
                {
                    value += 1.4;
                }

                if (value < 12)
                {
                    value += 30; // simulated rainfall so the demo does not stay at rock bottom
                }

                return Remember(sensor.Id, Math.Clamp(value, 5, 95));
            }

            if (name.Contains("temp"))
            {
                var value = 24 + 5 * Math.Sin(2 * Math.PI * elapsed / 600) + Noise(0.2);
                if (InSpike(sensor.Id, 0.004, 10))
                {
                    value = 39.5 + Noise(0.5); // above the 38 °C warning threshold
                }

                return Remember(sensor.Id, value);
            }

            if (name.Contains("humid"))
            {
                var value = 55 + 15 * Math.Sin(2 * Math.PI * elapsed / 480) + Noise(0.5);
                return Remember(sensor.Id, Math.Clamp(value, 25, 85));
            }

            var generic = Current(deviceId, sensor.Id, 50);
            generic += (50 - generic) * 0.05 + Noise(1);
            return Remember(sensor.Id, generic);
        }

        private int NextInt(Sensor sensor, string name, bool actuatorOn)
        {
            if (sensor.Category == SensorCategory.PowerConsumption || name.Contains("power"))
            {
                var watts = 420 + Noise(40) + (actuatorOn ? 350 : 0);
                if (InSpike(sensor.Id, 0.003, 3))
                {
                    watts = 2300 + Noise(100); // above the 2000 W high-draw threshold
                }

                return (int)Math.Round(Math.Max(0, watts));
            }

            return (int)Math.Round(100 + Noise(10));
        }

        private static bool IsWaterActuator(Sensor sensor)
        {
            var name = sensor.Name.ToLowerInvariant();
            return name.Contains("valve") || name.Contains("pump");
        }

        private bool IsOn(int deviceId, int sensorId)
        {
            var latest = _store.GetLatestTelemetry(deviceId, sensorId);
            return latest is not null
                && latest.ValueType == TelemetryValueType.Bool
                && latest.ValueJson == "true";
        }

        // Start from the last stored reading so seeded history flows into live data.
        private double Current(int deviceId, int sensorId, double fallback)
        {
            if (_values.TryGetValue(sensorId, out var known))
            {
                return known;
            }

            var latest = _store.GetLatestTelemetry(deviceId, sensorId);
            if (latest is not null &&
                double.TryParse(latest.ValueJson, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            {
                return parsed;
            }

            return fallback;
        }

        private double Remember(int sensorId, double value)
        {
            _values[sensorId] = value;
            return value;
        }

        private bool InSpike(int sensorId, double chancePerTick, int durationTicks)
        {
            if (_spikeTicks.TryGetValue(sensorId, out var remaining) && remaining > 0)
            {
                _spikeTicks[sensorId] = remaining - 1;
                return true;
            }

            if (_random.NextDouble() < chancePerTick)
            {
                _spikeTicks[sensorId] = durationTicks - 1;
                return true;
            }

            return false;
        }

        private double Noise(double amplitude) => (_random.NextDouble() * 2 - 1) * amplitude;
    }
}
