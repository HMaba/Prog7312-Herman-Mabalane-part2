using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartXApi.Models;
using SmartXApi.Services;

namespace SmartXApi.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/devices")]
    public class DevicesController : ControllerBase
    {
        private static readonly string[] AllowedImportExtensions = { ".txt", ".csv" };

        private readonly IDeviceService _deviceService;
        private readonly IDeviceImportService _importService;

        public DevicesController(IDeviceService deviceService, IDeviceImportService importService)
        {
            _deviceService = deviceService;
            _importService = importService;
        }

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
            var problem = await _deviceService.ValidateAsync(device);
            if (problem is not null)
            {
                return BadRequest(problem);
            }

            var created = await _deviceService.CreateAsync(device);
            return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
        }

        [HttpPut("{id:int}")]
        public async Task<IActionResult> Update(int id, Device device)
        {
            var problem = await _deviceService.ValidateAsync(device, id);
            if (problem is not null)
            {
                return BadRequest(problem);
            }

            var updated = await _deviceService.UpdateAsync(id, device);
            return updated ? NoContent() : NotFound();
        }

        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id)
        {
            var deleted = await _deviceService.DeleteAsync(id);
            return deleted ? NoContent() : NotFound();
        }

        [HttpPost("import")]
        [RequestSizeLimit(1_000_000)]
        public async Task<ActionResult<DeviceImportResult>> Import(IFormFile file)
        {
            if (file is null || file.Length == 0)
            {
                return BadRequest("The uploaded file is empty.");
            }

            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!AllowedImportExtensions.Contains(extension))
            {
                return BadRequest("Only .txt or .csv device lists are supported.");
            }

            await using var stream = file.OpenReadStream();
            var result = await _importService.ImportAsync(file.FileName, stream);

            if (result.Created + result.Updated == 0)
            {
                return BadRequest(result);
            }

            return Ok(result);
        }
    }
}
