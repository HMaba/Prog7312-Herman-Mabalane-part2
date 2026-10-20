using SmartxWPF.Models;
namespace SmartxWPF.Services;
public sealed class DashboardService(ApiClient api)
{
    public Task<DashboardDto> GetDashboardAsync(CancellationToken ct = default) => api.GetAsync<DashboardDto>("api/command-stream/dashboard", ct);
    public Task<List<AlertDto>> GetAlertsAsync(int take = 50, CancellationToken ct = default) => api.GetAsync<List<AlertDto>>($"api/command-stream/alerts?take={take}", ct);
    public async Task<List<InsightDto>> GetInsightsAsync(CancellationToken ct = default) => (await api.GetAsync<InsightsResponseDto>("api/command-stream/insights", ct)).Suggestions;
}
