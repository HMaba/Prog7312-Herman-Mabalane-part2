using SmartxWPF.Models;
namespace SmartxWPF.Services;
public sealed class TelemetryService(ApiClient api)
{
    public Task<List<SensorTimelineDto>> GetTimelineAsync(int deviceId, int sensorId, int take = 50, CancellationToken ct = default) =>
        api.GetAsync<List<SensorTimelineDto>>($"api/command-stream/timeline?deviceId={deviceId}&sensorId={sensorId}&take={take}", ct);
}
