using Microsoft.AspNetCore.Mvc;
using SmartAPI.Structures;

namespace SmartAPI.Controllers;

[ApiController]
[Route("api/command-stream")]
public sealed class CommandStreamController(CommandStreamStore store) : ControllerBase
{
    [HttpGet("devices")]
    public IActionResult GetDevices() => Ok(store.GetDevices());

    [HttpPost("devices")]
    public IActionResult RegisterDevice([FromBody] RegisteredDeviceRecord device)
    {
        if (string.IsNullOrWhiteSpace(device.NodeId) || string.IsNullOrWhiteSpace(device.Name) || string.IsNullOrWhiteSpace(device.Category))
            return BadRequest("NodeId, Name and Category are required.");
        return Ok(store.RegisterDevice(device));
    }

    [HttpPost("devices/upload")]
    [RequestSizeLimit(1_000_000)]
    public async Task<IActionResult> UploadDevices(IFormFile file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0) return BadRequest("Choose a non-empty .txt file.");
        if (!Path.GetExtension(file.FileName).Equals(".txt", StringComparison.OrdinalIgnoreCase)) return BadRequest("Only .txt files are accepted.");
        var imported = new List<RegisteredDeviceRecord>();
        var errors = new List<string>();
        using var reader = new StreamReader(file.OpenReadStream());
        var lineNumber = 0;
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith('#')) continue;
            var parts = line.Split('|', 3, StringSplitOptions.TrimEntries);
            if (parts.Length != 3 || parts.Any(string.IsNullOrWhiteSpace)) { errors.Add($"Line {lineNumber}: expected NodeId|Name|Category."); continue; }
            imported.Add(store.RegisterDevice(new RegisteredDeviceRecord { NodeId = parts[0], Name = parts[1], Category = parts[2], IsOnline = true }));
        }
        return Ok(new { ImportedCount = imported.Count, Devices = imported, Errors = errors });
    }

    [HttpPost("telemetry")]
    public IActionResult ReceiveTelemetry([FromBody] TelemetryPacketRecord packet)
    {
        if (string.IsNullOrWhiteSpace(packet.DeviceId) || string.IsNullOrWhiteSpace(packet.SensorId)) return BadRequest("DeviceId and SensorId are required.");
        try
        {
            store.AddTelemetry(packet);
            lock (store.SyncRoot)
            {
                if (store.CriticalAlertQueue.TryDequeue(out var critical, out _))
                    return Accepted(new { Status = "Critical alert prioritized", Packet = packet, CriticalAlert = critical });
            }
            return Accepted(new { Status = "Telemetry queued", Packet = packet });
        }
        catch (KeyNotFoundException ex) { return BadRequest(ex.Message); }
    }

    [HttpGet("telemetry/next")]
    public IActionResult GetNextTelemetry()
    {
        lock (store.SyncRoot)
            return store.TelemetryQueue.TryDequeue(out var packet) ? Ok(packet) : NoContent();
    }

    [HttpGet("alerts")]
    public IActionResult GetAlerts() { lock (store.SyncRoot) return Ok(store.AlertHistory.OrderByDescending(a => a.Timestamp).ToList()); }

    [HttpGet("timeline")]
    public IActionResult GetTimeline()
    {
        lock (store.SyncRoot) return Ok(store.SensorTimeline.SelectMany(pair => pair.Value).OrderBy(x => x.Timestamp).ToList());
    }

    [HttpPost("commands")]
    public IActionResult IssueCommand([FromBody] ManualCommandRecord command)
    {
        if (string.IsNullOrWhiteSpace(command.NodeId) || string.IsNullOrWhiteSpace(command.Command)) return BadRequest("NodeId and Command are required.");
        try { return Ok(store.IssueCommand(command)); }
        catch (KeyNotFoundException ex) { return BadRequest(ex.Message); }
    }

    [HttpPost("commands/undo")]
    public IActionResult UndoCommand()
    {
        var undone = store.UndoCommand();
        return undone is null ? NoContent() : Ok(new { Message = "Last command marked as undone. Send the inverse device command if physical rollback is required.", Command = undone });
    }

    [HttpPost("search")]
    public IActionResult Search([FromBody] SearchRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Query)) return BadRequest("Query is required.");
        return Ok(store.Search(request.Query.Trim()));
    }

    [HttpGet("insights")]
    public IActionResult GetInsights() => Ok(store.GetInsights());
}

public sealed class SearchRequest { public string Query { get; set; } = ""; }
