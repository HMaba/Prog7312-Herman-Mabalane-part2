using SmartXApi.Data;
using SmartXApi.Models;

namespace SmartXApi.Data
{
    public static class DataSeeder
    {
        public static void Seed(SmartXDbContext context)
        {
            if (context.Devices.Any())
            {
                return;
            }

            var esp32Soil = new Device
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
            };

            var esp32Power = new Device
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
            };

            context.Devices.AddRange(esp32Soil, esp32Power);
            context.SaveChanges();
        }
    }
}