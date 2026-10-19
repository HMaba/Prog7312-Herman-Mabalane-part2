using Microsoft.AspNetCore.Mvc;
using SmartAPI.Models;
using SmartAPI.Services;

namespace SmartAPI.Controllers
{

    [ApiController]
    [Route("api/devices")]
    public class DevicesController : ControllerBase
    {
        private readonly IDeviceService _deviceService;

        public DevicesController(IDeviceService deviceService) => _deviceService = deviceService;

        [HttpGet]
        public async Task<ActionResult<List<Device>>> GetAll() => Ok(await _deviceService.GetAllAsync());

        [HttpGet("{id:int}")]
        public async Task<ActionResult<Device>> GetById(int id)
        {
            var device = await _deviceService.GetByIdAsync(id);
            return device is null ? NotFound() : Ok(device);
        }

        [HttpPost]
        public async Task<ActionResult<Device>> Create(Device device)
        {
            var created = await _deviceService.CreateAsync(device);
            return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
        }

        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, Device device)
        {
            var updated = await _deviceService.UpdateAsync(id, device);
            return updated ? NoContent() : NotFound();
        }

        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id)
        {
            var deleted = await _deviceService.DeleteAsync(id);
            return deleted ? NoContent() : NotFound();
        }
    }
}