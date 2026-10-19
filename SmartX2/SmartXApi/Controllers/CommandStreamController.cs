using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SmartXApi.Models;
using SmartXApi.Security;
using SmartXApi.Services;

namespace SmartXApi.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/command-stream")]
    public class CommandStreamController : ControllerBase
    {
        private const int MaxBatchSize = 500;

        private readonly ITelemetryStreamProcessor _processor;
        private readonly ICommandStreamService _commands;
        private readonly ITelemetryService _telemetry;
        private readonly JsonSerializerOptions _jsonOptions;

        public CommandStreamController(
            ITelemetryStreamProcessor processor,
            ICommandStreamService commands,
            ITelemetryService telemetry,
            IOptions<Microsoft.AspNetCore.Mvc.JsonOptions> jsonOptions)
        {
            _processor = processor;
            _commands = commands;
            _telemetry = telemetry;
            _jsonOptions = jsonOptions.Value.JsonSerializerOptions;
        }

        [HttpGet("dashboard")]
        public ActionResult<DashboardSnapshot> GetDashboard() => Ok(_processor.GetDashboard(OperatorName()));

        [HttpGet("live")]
        public async Task Live(CancellationToken cancellationToken)
        {
            Response.Headers.ContentType = "text/event-stream";
            Response.Headers.CacheControl = "no-cache";
            Response.Headers["X-Accel-Buffering"] = "no";

            var user = OperatorName();
            var lastVersion = -1L;
            var lastSent = DateTime.MinValue;

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    var version = _processor.CurrentVersion;
                    if (version != lastVersion || DateTime.UtcNow - lastSent > TimeSpan.FromSeconds(5))
                    {
                        var snapshot = _processor.GetDashboard(user);
                        var json = JsonSerializer.Serialize(snapshot, _jsonOptions);

                        await Response.WriteAsync($"event: snapshot\ndata: {json}\n\n", cancellationToken);
                        await Response.Body.FlushAsync(cancellationToken);

                        lastVersion = version;
                        lastSent = DateTime.UtcNow;
                    }

                    await Task.Delay(500, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        [HttpGet("devices")]
        public ActionResult<List<DeviceSnapshot>> GetDevices([FromQuery] DeviceFilter filter) =>
            Ok(_processor.FilterDevices(filter));

        [HttpPost("devices/search")]
        public ActionResult<List<DeviceSnapshot>> Search([FromBody] DeviceFilter filter) =>
            Ok(_commands.Search(filter, OperatorName()));

        [HttpGet("devices/{deviceId:int}")]
        public ActionResult<DeviceSnapshot> GetDevice(int deviceId)
        {
            var device = _processor.GetDevice(deviceId);
            return device is null ? NotFound() : Ok(device);
        }

        [HttpGet("timeline")]
        public ActionResult<List<SensorTimelineEntry>> GetTimeline([FromQuery] int deviceId, [FromQuery] int sensorId, [FromQuery] int take = 100) =>
            Ok(_processor.GetTimeline(deviceId, sensorId, take));

        [HttpGet("alerts")]
        public ActionResult<List<StreamAlert>> GetAlerts([FromQuery] int take = 50) =>
            Ok(_processor.GetRecentAlerts(take));

        [HttpGet("insights")]
        public ActionResult<InsightsResponse> GetInsights() => Ok(_processor.GetInsights(OperatorName()));

        [HttpPost("commands")]
        public async Task<ActionResult<CommandOutcome>> IssueCommand([FromBody] CommandRequest request)
        {
            var outcome = await _commands.IssueAsync(request, OperatorName());
            return ToResult(outcome);
        }

        [HttpPost("commands/undo")]
        public async Task<ActionResult<CommandOutcome>> UndoLastCommand()
        {
            var outcome = await _commands.UndoAsync(OperatorName());
            return ToResult(outcome);
        }

        [HttpGet("commands/history")]
        public ActionResult<List<OverrideCommand>> GetCommandHistory([FromQuery] int take = 50) =>
            Ok(_processor.GetCommandHistory(take));

        [HttpPost("telemetry/batch")]
        [Authorize(Policy = DeviceKeyDefaults.IngestPolicy)]
        public async Task<ActionResult<BatchIngestResult>> IngestBatch([FromBody] List<TelemetrySubmission> submissions)
        {
            if (submissions.Count == 0)
            {
                return BadRequest("The batch is empty.");
            }

            if (submissions.Count > MaxBatchSize)
            {
                return BadRequest($"A batch cannot contain more than {MaxBatchSize} readings.");
            }

            var batch = new BatchIngestResult();

            for (var i = 0; i < submissions.Count; i++)
            {
                var item = new BatchItemResult { Index = i };

                if (!TelemetryPacketFactory.TryCreate(submissions[i], out var packet, out var error) || packet is null)
                {
                    item.IsValid = false;
                    item.Message = error;
                }
                else
                {
                    var result = await _telemetry.ReceiveAsync(packet);
                    item.IsValid = result.IsValid;
                    item.Message = result.Message;
                }

                if (item.IsValid)
                {
                    batch.Accepted++;
                }
                else
                {
                    batch.Rejected++;
                }

                batch.Results.Add(item);
            }

            return Ok(batch);
        }

        private ActionResult ToResult(CommandOutcome outcome) => outcome.Kind switch
        {
            CommandOutcomeKind.Success => Ok(outcome),
            CommandOutcomeKind.NothingToUndo => Ok(outcome),
            CommandOutcomeKind.NotFound => NotFound(outcome),
            CommandOutcomeKind.Conflict => Conflict(outcome),
            _ => BadRequest(outcome)
        };

        private string OperatorName() =>
            User.FindFirstValue("email")
            ?? User.FindFirstValue("user_id")
            ?? User.FindFirstValue("sub")
            ?? User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? "operator";
    }
}
