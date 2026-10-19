using Microsoft.EntityFrameworkCore;
using SmartXApi.Data;
using SmartXApi.Models;

namespace SmartXApi.Services
{

    public interface IAttachmentService
    {
        Task<List<Attachment>> GetByDeviceIdAsync(int deviceId);
        Task<Attachment?> SaveAsync(int deviceId, AttachmentType type, string fileName, Stream content);
        Task<bool> DeleteAsync(int id);
        string StorageRoot { get; }
    }
    public class AttachmentService : IAttachmentService
    {
        private readonly SmartXDbContext _context;
        public string StorageRoot { get; }

        public AttachmentService(SmartXDbContext context, IWebHostEnvironment env)
        {
            _context = context;
            StorageRoot = Path.Combine(env.ContentRootPath, "AttachmentStorage");
            Directory.CreateDirectory(StorageRoot);
        }

        public async Task<List<Attachment>> GetByDeviceIdAsync(int deviceId)
        {
            return await _context.Attachments.AsNoTracking().Where(a => a.DeviceId == deviceId).ToListAsync();
        }

        public async Task<Attachment?> SaveAsync(int deviceId, AttachmentType type, string fileName, Stream content)
        {
            var deviceExists = await _context.Devices.AnyAsync(d => d.Id == deviceId);
            if (!deviceExists)
            {
                return null;
            }

            var safeName = $"{deviceId}_{DateTime.UtcNow.Ticks}_{Path.GetFileName(fileName)}";
            var fullPath = Path.Combine(StorageRoot, safeName);

            await using (var fileStream = File.Create(fullPath))
            {
                await content.CopyToAsync(fileStream);
            }

            var attachment = new Attachment
            {
                DeviceId = deviceId,
                FileName = fileName,
                FileType = type,
                FilePath = fullPath,
                FileSizeBytes = new FileInfo(fullPath).Length,
                UploadedAt = DateTime.UtcNow
            };

            _context.Attachments.Add(attachment);
            await _context.SaveChangesAsync();
            return attachment;
        }

        public async Task<bool> DeleteAsync(int id)
        {
            var attachment = await _context.Attachments.FirstOrDefaultAsync(a => a.Id == id);
            if (attachment is null)
            {
                return false;
            }

            if (File.Exists(attachment.FilePath))
            {
                File.Delete(attachment.FilePath);
            }

            _context.Attachments.Remove(attachment);
            await _context.SaveChangesAsync();
            return true;
        }
    }
}