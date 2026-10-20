using SmartXApi.Models;
using System.Text;

namespace SmartXApi.Data
{
    public static class DataSeeder
    {
        public static void Seed(IDataStore store)
        {
            if (store.GetDevices(includeChildren: false).Count > 0)
            {
                return;
            }

            var soilNode = store.AddDevice(new Device
            {
                DeviceIdentifier = "ESP32-001",
                MacAddress = "24:6F:28:AA:BB:01",
                Location = "Greenhouse A",
                Room = "Zone 1",
                NodeId = "NODE-A1",
                IsOnline = true,
                Sensors = new List<Sensor>
                {
                    new() { Name = "Soil Moisture", Category = SensorCategory.Environmental, ValueType = TelemetryValueType.Float, Unit = "%" },
                    new() { Name = "Ambient Temperature", Category = SensorCategory.Environmental, ValueType = TelemetryValueType.Float, Unit = "°C" }
                }
            });

            var powerNode = store.AddDevice(new Device
            {
                DeviceIdentifier = "ESP32-002",
                MacAddress = "24:6F:28:AA:BB:02",
                Location = "Utility Room",
                Room = "Zone 2",
                NodeId = "NODE-B1",
                IsOnline = true,
                Sensors = new List<Sensor>
                {
                    new() { Name = "Power Meter", Category = SensorCategory.PowerConsumption, ValueType = TelemetryValueType.Int, Unit = "W" },
                    new() { Name = "Irrigation Valve", Category = SensorCategory.Actuator, ValueType = TelemetryValueType.Bool, Unit = "on/off" }
                }
            });

            var moisture = soilNode.Sensors.First(s => s.Name == "Soil Moisture");
            var temperature = soilNode.Sensors.First(s => s.Name == "Ambient Temperature");
            var meter = powerNode.Sensors.First(s => s.Name == "Power Meter");
            var valve = powerNode.Sensors.First(s => s.Name == "Irrigation Valve");

            var now = DateTime.UtcNow;
            float[] moistureSeries = { 41.2f, 40.5f, 39.8f, 38.9f, 37.6f, 36.4f };
            float[] temperatureSeries = { 22.4f, 22.9f, 23.5f, 24.1f, 24.6f, 25.0f };
            int[] powerSeries = { 410, 395, 520, 480, 455, 430 };
            bool[] valveSeries = { false, false, true, true, false, false };

            for (var i = 0; i < moistureSeries.Length; i++)
            {
                var timestamp = now.AddMinutes(-(moistureSeries.Length - i) * 5);

                AddReading(store, new TelemetryPacket<float> { DeviceId = soilNode.Id, SensorId = moisture.Id, Timestamp = timestamp, Value = moistureSeries[i] });
                AddReading(store, new TelemetryPacket<float> { DeviceId = soilNode.Id, SensorId = temperature.Id, Timestamp = timestamp, Value = temperatureSeries[i] });
                AddReading(store, new TelemetryPacket<int> { DeviceId = powerNode.Id, SensorId = meter.Id, Timestamp = timestamp, Value = powerSeries[i] });
                AddReading(store, new TelemetryPacket<bool> { DeviceId = powerNode.Id, SensorId = valve.Id, Timestamp = timestamp, Value = valveSeries[i] });
            }

            var config = Encoding.UTF8.GetBytes(
                "# ESP32-001 node configuration\n" +
                "sample_interval_seconds=30\n" +
                "moisture_low_threshold=30\n" +
                "temperature_high_threshold=35\n");

            store.AddAttachment(new Attachment
            {
                DeviceId = soilNode.Id,
                FileName = "esp32-001-config.txt",
                FileType = AttachmentType.ConfigFile,
                UploadedAt = now.AddDays(-1)
            }, config);

            var hardwareLog = Encoding.UTF8.GetBytes(
                "boot ok\n" +
                "wifi connected rssi=-58\n" +
                "relay self-test passed\n");

            store.AddAttachment(new Attachment
            {
                DeviceId = powerNode.Id,
                FileName = "esp32-002-hardware.log",
                FileType = AttachmentType.HardwareLog,
                UploadedAt = now.AddHours(-6)
            }, hardwareLog);
        }

        private static void AddReading(IDataStore store, TelemetryPacketBase packet) =>
            store.AddTelemetry(TelemetryRecord.FromPacket(packet));
    }
}
