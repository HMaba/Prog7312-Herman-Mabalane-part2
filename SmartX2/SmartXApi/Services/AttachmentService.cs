using SmartXApi.Data;
using SmartXApi.Models;
using Attachment = SmartXApi.Models.Attachment;

namespace SmartXApi.Services
{
    public interface IAttachmentService
    {
        Task<List<Attachment>> GetByDeviceIdAsync(int deviceId);
        Task<Attachment?> SaveAsync(int deviceId, AttachmentType type, string fileName, Stream content);
        Task<(Attachment Info, byte[] Content)?> GetContentAsync(int id);
        Task<bool> DeleteAsync(int id);
    }

    /// <summary>
    /// Attachments (metadata and file bytes) live in memory only. Nothing is written to disk.
    /// </summary>
    public class AttachmentService : IAttachmentService
    {
        private readonly IDataStore _store;

        public AttachmentService(IDataStore store) => _store = store;

        public Task<List<Attachment>> GetByDeviceIdAsync(int deviceId) =>
            Task.FromResult(_store.GetAttachments(deviceId));

        public async Task<Attachment?> SaveAsync(int deviceId, AttachmentType type, string fileName, Stream content)
        {
            if (!_store.DeviceExists(deviceId))
            {
                return null;
            }

            using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer);

            return _store.AddAttachment(new Attachment
            {
                DeviceId = deviceId,
                FileName = Path.GetFileName(fileName),
                FileType = type,
                UploadedAt = DateTime.UtcNow
            }, buffer.ToArray());
        }

        public Task<(Attachment Info, byte[] Content)?> GetContentAsync(int id)
        {
            var info = _store.GetAttachment(id);
            var bytes = _store.GetAttachmentContent(id);

            (Attachment Info, byte[] Content)? result = info is null || bytes is null ? null : (info, bytes);
            return Task.FromResult(result);
        }

        public Task<bool> DeleteAsync(int id) => Task.FromResult(_store.RemoveAttachment(id));
    }
}
