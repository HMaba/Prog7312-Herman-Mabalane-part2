namespace SmartXApi.Models
{
    public class Attachment
    {
        public int Id { get; set; }
        public int DeviceId { get; set; }
        public Device? Device { get; set; }

        public string FileName { get; set; } = string.Empty;
        public AttachmentType FileType { get; set; }

        public string FilePath { get; set; } = string.Empty;

        public long FileSizeBytes { get; set; }
        public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
    }
    public enum AttachmentType
    {
        ConfigFile,
        DeploymentPhoto,
        HardwareLog
    }
}