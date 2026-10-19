using Microsoft.AspNetCore.Mvc;
using SmartAPI.Models;
using SmartAPI.Services;

namespace SmartAPI.Controllers
{

    [ApiController]
    [Route("api/sensors")]
    public class SensorsController : ControllerBase
    {
        private readonly ISensorService _sensorService;

        public SensorsController(ISensorService sensorService) => _sensorService = sensorService;

        [HttpGet]
        public async Task<ActionResult<List<Sensor>>> GetAll([FromQuery] int? deviceId)
        {
            var sensors = deviceId.HasValue
                ? await _sensorService.GetByDeviceIdAsync(deviceId.Value)
                : await _sensorService.GetAllAsync();

            return Ok(sensors);
        }

        [HttpGet("{id:int}")]
        public async Task<ActionResult<Sensor>> GetById(int id)
        {
            var sensor = await _sensorService.GetByIdAsync(id);
            return sensor is null ? NotFound() : Ok(sensor);
        }

        [HttpPost]
        public async Task<ActionResult<Sensor>> Create(Sensor sensor)
        {
            var created = await _sensorService.CreateAsync(sensor);
            return created is null
                ? BadRequest($"Device {sensor.DeviceId} does not exist.")
                : CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
        }

        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, Sensor sensor)
        {
            var updated = await _sensorService.UpdateAsync(id, sensor);
            return updated ? NoContent() : NotFound();
        }

        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id)
        {
            var deleted = await _sensorService.DeleteAsync(id);
            return deleted ? NoContent() : NotFound();
        }
    }
}