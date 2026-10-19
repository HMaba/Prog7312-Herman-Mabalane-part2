using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartXApi.Models;
using SmartXApi.Security;
using SmartXApi.Services;

namespace SmartXApi.Controllers
{
    [ApiController]
    [Route("api/telemetry")]
    public class TelemetryController : ControllerBase
    {
        private readonly ITelemetryService _telemetryService;

        public TelemetryController(ITelemetryService telemetryService) => _telemetryService = telemetryService;

        [HttpPost]
        [Authorize(Policy = DeviceKeyDefaults.IngestPolicy)]
        public async Task<ActionResult<TelemetryResult>> Post(TelemetrySubmission submission)
        {
            if (!TelemetryPacketFactory.TryCreate(submission, out var packet, out var error) || packet is null)
            {
                return BadRequest(new TelemetryResult { IsValid = false, Message = error });
            }

            var result = await _telemetryService.ReceiveAsync(packet);
            return result.IsValid ? Ok(result) : BadRequest(result);
        }

        [HttpGet("history")]
        [Authorize]
        public async Task<ActionResult<List<object>>> GetHistory([FromQuery] int deviceId, [FromQuery] int? sensorId, [FromQuery] int take = 100)
        {
            var history = await _telemetryService.GetHistoryAsync(deviceId, sensorId, Math.Clamp(take, 1, 1000));
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
        [Authorize]
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
        [Authorize]
        public async Task<ActionResult<PowerReading>> GetAggregatePower(int deviceId)
        {
            var total = await _telemetryService.GetAggregatePowerAsync(deviceId);
            return Ok(new { WattsTotal = total.Watts, Display = total.ToString() });
        }
    }
}
