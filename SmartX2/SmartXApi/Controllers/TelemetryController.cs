using Microsoft.AspNetCore.Mvc;
using SmartAPI.Models;
using SmartAPI.Services;

namespace SmartAPI.Controllers
{

    [ApiController]
    [Route("api/telemetry")]
    public class TelemetryController : ControllerBase
    {
        private readonly ITelemetryService _telemetryService;

        public TelemetryController(ITelemetryService telemetryService) => _telemetryService = telemetryService;

        /// <summary>
        /// Receives a telemetry reading. The submission's ValueType determines which
        /// concrete TelemetryPacket&lt;T&gt; is constructed and validated/processed.
        /// </summary>
        [HttpPost]
        public async Task<ActionResult<TelemetryResult>> Post(TelemetrySubmission submission)
        {
            TelemetryPacketBase packet = submission.ValueType switch
            {
                TelemetryValueType.Float => new TelemetryPacket<float>
                {
                    DeviceId = submission.DeviceId,
                    SensorId = submission.SensorId,
                    Timestamp = submission.Timestamp ?? DateTime.UtcNow,
                    Value = float.Parse(submission.Value)
                },
                TelemetryValueType.Int => new TelemetryPacket<int>
                {
                    DeviceId = submission.DeviceId,
                    SensorId = submission.SensorId,
                    Timestamp = submission.Timestamp ?? DateTime.UtcNow,
                    Value = int.Parse(submission.Value)
                },
                TelemetryValueType.Bool => new TelemetryPacket<bool>
                {
                    DeviceId = submission.DeviceId,
                    SensorId = submission.SensorId,
                    Timestamp = submission.Timestamp ?? DateTime.UtcNow,
                    Value = bool.Parse(submission.Value)
                },
                _ => new TelemetryPacket<string>
                {
                    DeviceId = submission.DeviceId,
                    SensorId = submission.SensorId,
                    Timestamp = submission.Timestamp ?? DateTime.UtcNow,
                    Value = submission.Value
                }
            };

            var result = await _telemetryService.ReceiveAsync(packet);
            return result.IsValid ? Ok(result) : BadRequest(result);
        }

        [HttpGet("history")]
        public async Task<ActionResult<List<object>>> GetHistory([FromQuery] int deviceId, [FromQuery] int? sensorId, [FromQuery] int take = 100)
        {
            var history = await _telemetryService.GetHistoryAsync(deviceId, sensorId, take);
            return Ok(history.Select(p => new
            {
                p.DeviceId,
                p.SensorId,
                p.Timestamp,
                ValueType = p.ValueType.ToString(),
                Value = p.GetValue()
            }));
        }

        [HttpGet("latest")]
        public async Task<ActionResult> GetLatest([FromQuery] int deviceId, [FromQuery] int sensorId)
        {
            var latest = await _telemetryService.GetLatestAsync(deviceId, sensorId);
            if (latest is null)
            {
                return NotFound();
            }

            return Ok(new
            {
                latest.DeviceId,
                latest.SensorId,
                latest.Timestamp,
                ValueType = latest.ValueType.ToString(),
                Value = latest.GetValue()
            });
        }

        [HttpGet("power/{deviceId:int}")]
        public async Task<ActionResult<PowerReading>> GetAggregatePower(int deviceId)
        {
            var total = await _telemetryService.GetAggregatePowerAsync(deviceId);
            return Ok(new { WattsTotal = total.Watts, Display = total.ToString() });
        }
    }
}