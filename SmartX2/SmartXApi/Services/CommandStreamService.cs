using SmartXApi.Models;
using SmartXApi.Data;

namespace SmartXApi.Services
{
    public interface ICommandStreamService
    {
        Task<CommandOutcome> IssueAsync(CommandRequest request, string operatorName);
        Task<CommandOutcome> UndoAsync(string operatorName);
        List<DeviceSnapshot> Search(DeviceFilter filter, string operatorName);
    }

    public class CommandStreamService : ICommandStreamService
    {
        private readonly SmartXDbContext _context;
        private readonly ITelemetryStreamProcessor _processor;
        private readonly ILogger<CommandStreamService> _logger;

        public CommandStreamService(
            SmartXDbContext context,
            ITelemetryStreamProcessor processor,
            ILogger<CommandStreamService> logger)
        {
            _context = context;
            _processor = processor;
            _logger = logger;
        }

        public async Task<CommandOutcome> IssueAsync(CommandRequest request, string operatorName)
        {
            var outcome = _processor.ApplyOverride(request, operatorName);
            await PersistAsync(outcome);
            return outcome;
        }

        public async Task<CommandOutcome> UndoAsync(string operatorName)
        {
            var outcome = _processor.UndoLastOverride(operatorName);
            await PersistAsync(outcome);
            return outcome;
        }

        public List<DeviceSnapshot> Search(DeviceFilter filter, string operatorName)
        {
            var results = _processor.FilterDevices(filter);
            _processor.RecordSearch(operatorName, filter);
            return results;
        }

        private async Task PersistAsync(CommandOutcome outcome)
        {
            if (!outcome.Success || outcome.Command is null)
            {
                return;
            }

            try
            {
                var packet = new TelemetryPacket<bool>
                {
                    DeviceId = outcome.Command.DeviceId,
                    SensorId = outcome.Command.SensorId,
                    Timestamp = outcome.Command.IssuedAt,
                    Value = outcome.Command.NewState
                };

                _context.TelemetryRecords.Add(TelemetryRecord.FromPacket(packet));
                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Command {CommandId} was applied but its telemetry record could not be saved.", outcome.Command.Id);
                outcome.Message += " The state change was applied but could not be written to the telemetry history.";
            }
        }
    }
}
