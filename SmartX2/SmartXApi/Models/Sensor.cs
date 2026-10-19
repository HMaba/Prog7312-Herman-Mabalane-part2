using SmartXApi.Models;


namespace SmartXApi.Models
{

     public class Sensor
     {
        public int Id { get; set; }

        public int DeviceId { get; set; }

        public Device? Device { get; set; }

        public string Name { get; set; } = string.Empty;

        public SensorCategory Category { get; set; }

        public TelemetryValueType ValueType { get; set; } = TelemetryValueType.Float;

        public string Unit { get; set; } = string.Empty;
     }
}