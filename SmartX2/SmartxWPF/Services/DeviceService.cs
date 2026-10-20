using SmartxWPF.Models;
namespace SmartxWPF.Services;
public sealed class DeviceService(ApiClient api)
{
    public Task<List<DeviceDto>> GetDevicesAsync(CancellationToken ct = default) => api.GetAsync<List<DeviceDto>>("api/command-stream/devices", ct);
    public Task<List<DeviceDto>> SearchAsync(string? text, CancellationToken ct = default) => api.PostJsonAsync<DeviceFilterDto, List<DeviceDto>>("api/command-stream/devices/search", new DeviceFilterDto { Text = text }, ct);
}
