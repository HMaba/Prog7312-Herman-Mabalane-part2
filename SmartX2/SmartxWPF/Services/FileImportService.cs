using SmartxWPF.Models;
using System.IO;
namespace SmartxWPF.Services;
public sealed class FileImportService(ApiClient api)
{
    public Task<DeviceImportResultDto> ImportAsync(string filePath, CancellationToken ct = default)
    {
        var ext = Path.GetExtension(filePath);
        if (!ext.Equals(".txt", StringComparison.OrdinalIgnoreCase) && !ext.Equals(".csv", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Choose a .txt or .csv device list.");
        return api.UploadFileAsync<DeviceImportResultDto>("api/devices/import", filePath, ct);
    }
}
