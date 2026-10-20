using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartXApi.Data;
using SmartXApi.Services;

namespace SmartXApi.Controllers
{
    /// <summary>
    /// Development-only health view, readable in a browser without signing in:
    ///   http://localhost:5234/api/diagnostics
    /// It shows whether the simulator is producing readings and whether the live stream is receiving them,
    /// which separates "the API has no data" from "the WPF app cannot see the data".
    /// Returns 404 outside the Development environment.
    /// </summary>
    [ApiController]
    [AllowAnonymous]
    [Route("api/diagnostics")]
    public class DiagnosticsController : ControllerBase
    {
        private readonly IWebHostEnvironment _environment;
        private readonly IDataStore _store;
        private readonly ITelemetryStreamProcessor _processor;
        private readonly TelemetrySimulator _simulator;

        public DiagnosticsController(
            IWebHostEnvironment environment,
            IDataStore store,
            ITelemetryStreamProcessor processor,
            TelemetrySimulator simulator)
        {
            _environment = environment;
            _store = store;
            _processor = processor;
            _simulator = simulator;
        }

        [HttpGet]
        public IActionResult Get()
        {
            if (!_environment.IsDevelopment())
            {
                return NotFound();
            }

            var dashboard = _processor.GetDashboard("diagnostics");

            return Ok(new
            {
                simulator = new
                {
                    enabled = _simulator.Enabled,
                    intervalSeconds = _simulator.IntervalSeconds,
                    ticks = _simulator.Ticks,
                    readingsAccepted = _simulator.ReadingsAccepted,
                    readingsRejected = _simulator.ReadingsRejected,
                    lastTickUtc = _simulator.LastTickUtc,
                    lastProblem = _simulator.LastProblem
                },
                store = new
                {
                    devices = _store.GetDevices(includeChildren: false).Count,
                    sensors = _store.GetSensors().Count
                },
                stream = new
                {
                    registeredDevices = dashboard.Stats.RegisteredDevices,
                    processedPackets = dashboard.Stats.ProcessedPackets,
                    activeAlerts = dashboard.Stats.ActiveErrorStates,
                    disconnectedNodes = dashboard.Stats.DisconnectedNodes
                },
                devices = dashboard.Devices.Select(d => new
                {
                    d.DeviceId,
                    d.DeviceIdentifier,
                    status = d.Status,
                    alertState = d.AlertState,
                    d.PacketsReceived,
                    lastSeenUtc = d.LastSeenUtc,
                    sensors = d.Sensors.Select(s => new { s.Id, s.Name, s.Value, s.Unit, s.Timestamp })
                })
            });
        }
    }
}
