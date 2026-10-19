namespace SmartXApi.Models
{
    public readonly struct PowerReading : IComparable<PowerReading>
    {
        public double Watts { get; }

        public PowerReading(double watts) => Watts = watts;

        public static PowerReading operator +(PowerReading a, PowerReading b) => new(a.Watts + b.Watts);

        public static PowerReading operator -(PowerReading a, PowerReading b) => new(Math.Max(0, a.Watts - b.Watts));

        public static bool operator >(PowerReading a, PowerReading b) => a.Watts > b.Watts;

        public static bool operator <(PowerReading a, PowerReading b) => a.Watts < b.Watts;

        public static implicit operator PowerReading(double watts) => new(watts);

        public int CompareTo(PowerReading other) => Watts.CompareTo(other.Watts);

        public override string ToString() => $"{Watts:0.##} W";
    }
}