using Microsoft.AspNetCore.Mvc;
using SmartXApi.Models;
using SmartXApi.Services;

namespace SmartXApi.Controllers
{

    [ApiController]
    [Route("api/attachments")]
    public class AttachmentsController : ControllerBase
    {
        private readonly IAttachmentService _attachmentService;

        public AttachmentsController(IAttachmentService attachmentService) => _attachmentService = attachmentService;

        [HttpGet]
        public async Task<ActionResult<List<Attachment>>> GetByDevice([FromQuery] int deviceId) =>
            Ok(await _attachmentService.GetByDeviceIdAsync(deviceId));

        [HttpPost]
        [RequestSizeLimit(50_000_000)]
        public async Task<ActionResult<Attachment>> Upload([FromForm] int deviceId, [FromForm] AttachmentType fileType, IFormFile file)
        {
            if (file.Length == 0)
            {
                return BadRequest("Empty file.");
            }

            await using var stream = file.OpenReadStream();
            var attachment = await _attachmentService.SaveAsync(deviceId, fileType, file.FileName, stream);

            return attachment is null
                ? BadRequest($"Device {deviceId} does not exist.")
                : CreatedAtAction(nameof(GetByDevice), new { deviceId }, attachment);
        }

        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Delete(int id)
        {
            var deleted = await _attachmentService.DeleteAsync(id);
            return deleted ? NoContent() : NotFound();
        }
    }
}