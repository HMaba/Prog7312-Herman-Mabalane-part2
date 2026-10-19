using SmartXApi.Models;
using System.Globalization;

namespace SmartXApi.Services
{
    public sealed class ThresholdRule
    {
        public ThresholdRule(string alertType, AlertSeverity severity, double? min, double? max)
        {
            AlertType = alertType;
            Severity = severity;
            Min = min;
            Max = max;
        }

        public string AlertType { get; }
        public AlertSeverity Severity { get; }
        public double? Min { get; }
        public double? Max { get; }

        public bool IsViolated(double value) =>
            (Min.HasValue && value < Min.Value) || (Max.HasValue && value > Max.Value);

        public string Describe(string unit)
        {
            if (Min.HasValue && Max.HasValue)
            {
                return $"outside {ThresholdCatalog.Format(Min.Value)}-{ThresholdCatalog.Format(Max.Value)}{unit}";
            }

            return Min.HasValue
                ? $"below {ThresholdCatalog.Format(Min.Value)}{unit}"
                : $"above {ThresholdCatalog.Format(Max!.Value)}{unit}";
        }
    }

    public static class ThresholdCatalog
    {
        private static readonly IReadOnlyList<ThresholdRule> Empty = Array.Empty<ThresholdRule>();

        private static readonly IReadOnlyList<ThresholdRule> Moisture = new[]
        {
            new ThresholdRule("CriticalMoistureDrop", AlertSeverity.Critical, 20, null),
            new ThresholdRule("LowMoisture", AlertSeverity.Warning, 30, null)
        };

        private static readonly IReadOnlyList<ThresholdRule> Temperature = new[]
        {
            new ThresholdRule("ExtremeTemperature", AlertSeverity.Critical, -5, 45),
            new ThresholdRule("TemperatureOutOfRange", AlertSeverity.Warning, 5, 38)
        };

        private static readonly IReadOnlyList<ThresholdRule> Humidity = new[]
        {
            new ThresholdRule("HumidityOutOfRange", AlertSeverity.Warning, 20, 90)
        };

        private static readonly IReadOnlyList<ThresholdRule> Power = new[]
        {
            new ThresholdRule("PowerSpike", AlertSeverity.Critical, null, 3000),
            new ThresholdRule("HighPowerDraw", AlertSeverity.Warning, null, 2000)
        };

        public static string Format(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

        public static IReadOnlyList<ThresholdRule> RulesFor(SensorInfo sensor)
        {
            if (sensor.Category == SensorCategory.Actuator ||
                sensor.ValueType == TelemetryValueType.Bool ||
                sensor.ValueType == TelemetryValueType.String)
            {
                return Empty;
            }

            var name = sensor.Name.ToLowerInvariant();

            if (name.Contains("moisture"))
            {
                return Moisture;
            }

            if (name.Contains("temp"))
            {
                return Temperature;
            }

            if (name.Contains("humid"))
            {
                return Humidity;
            }

            if (sensor.Category == SensorCategory.PowerConsumption)
            {
                return Power;
            }

            return Empty;
        }

        public static ThresholdRule? Worst(SensorInfo sensor, double value)
        {
            ThresholdRule? worst = null;
            foreach (var rule in RulesFor(sensor))
            {
                if (rule.IsViolated(value) && (worst is null || rule.Severity < worst.Severity))
                {
                    worst = rule;
                }
            }

            return worst;
        }
    }
}
