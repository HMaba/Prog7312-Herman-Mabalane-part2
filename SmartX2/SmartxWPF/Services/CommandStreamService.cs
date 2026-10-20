using SmartxWPF.Models;
namespace SmartxWPF.Services;
public sealed class CommandStreamService(ApiClient api)
{
    public Task<List<CommandDto>> GetHistoryAsync(int take = 50, CancellationToken ct = default) => api.GetAsync<List<CommandDto>>($"api/command-stream/commands/history?take={take}", ct);
    public Task<CommandOutcomeDto> SendAsync(CommandRequestDto request, CancellationToken ct = default) => api.PostJsonAsync<CommandRequestDto, CommandOutcomeDto>("api/command-stream/commands", request, ct);
    public Task<CommandOutcomeDto> UndoAsync(CancellationToken ct = default) => api.PostEmptyAsync<CommandOutcomeDto>("api/command-stream/commands/undo", ct);
}
