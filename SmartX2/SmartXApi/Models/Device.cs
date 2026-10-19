

namespace SmartXApi.Models
{

    public class Device
    {
        public int Id { get; set; }

        public string DeviceIdentifier { get; set; } = string.Empty;

        public string MacAddress { get; set; } = string.Empty;

        public string Location { get; set; } = string.Empty;

        public string Room { get; set; } = string.Empty;

        public string NodeId { get; set; } = string.Empty;

        public bool IsOnline { get; set; }

        public DateTime RegisteredAt { get; set; } = DateTime.UtcNow;

        public List<Sensor> Sensors { get; set; } = new();

        public List<Attachment> Attachments { get; set; } = new();
    }
}