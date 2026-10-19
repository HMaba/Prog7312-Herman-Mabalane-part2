using SmartXApi.Models;
using SmartXApi.Services;
using SmartXApi.Controllers;
using SmartXApi.Data;

namespace SmartXApi.Services
{

    public interface ITelemetryProcessingService
    {
        TelemetryResult Process(TelemetryPacketBase packet);
        PowerReading AggregatePower(IEnumerable<PowerReading> readings);
    }
    public class TelemetryProcessingService : ITelemetryProcessingService
    {
        public TelemetryResult Process(TelemetryPacketBase packet)
        {
            
            object processed = packet switch
            {
                TelemetryPacket<float> f => MathF.Round(f.Value, 2),
                TelemetryPacket<int> i => i.Value,
                TelemetryPacket<bool> b => b.Value,
                _ => packet.GetValue()
            };

            return new TelemetryResult
            {
                IsValid = true,
                Message = "Processed",
                ProcessedValue = processed
            };
        }

        public PowerReading AggregatePower(IEnumerable<PowerReading> readings)
        {
            var total = new PowerReading(0);
            foreach (var reading in readings)
            {
                total += reading; 
            }

            return total;
        }
    }
}