using System;

namespace Common
{
    public class BatteryEventArgs : EventArgs
    {
        public string BatteryId { get; set; }
        public string TestId { get; set; }
        public int SocPercent { get; set; }
        public string Message { get; set; }
        public DateTime Timestamp { get; set; }
        public string AlertType { get; set; }
        public double Value { get; set; }
        public double Threshold { get; set; }

        public BatteryEventArgs()
        {
            Timestamp = DateTime.UtcNow;
        }

        public BatteryEventArgs(string batteryId, string testId, int socPercent, string message) : this()
        {
            BatteryId = batteryId;
            TestId = testId;
            SocPercent = socPercent;
            Message = message;
        }
    }

    public class VoltageEventArgs : BatteryEventArgs
    {
        public double VoltageChange { get; set; }
        public double PreviousVoltage { get; set; }
        public double CurrentVoltage { get; set; }
        public string Direction { get; set; }

        public VoltageEventArgs(string batteryId, string testId, int socPercent,
            double voltageChange, double previousVoltage, double currentVoltage, string direction)
            : base(batteryId, testId, socPercent, $"Voltage spike detected: ΔV={voltageChange:F3}V ({direction})")
        {
            VoltageChange = voltageChange;
            PreviousVoltage = previousVoltage;
            CurrentVoltage = currentVoltage;
            Direction = direction;
            AlertType = "VoltageSpike";
            Value = Math.Abs(voltageChange);
        }
    }

    public class ImpedanceEventArgs : BatteryEventArgs
    {
        public double ImpedanceChange { get; set; }
        public double PreviousImpedance { get; set; }
        public double CurrentImpedance { get; set; }
        public string Direction { get; set; }

        public ImpedanceEventArgs(string batteryId, string testId, int socPercent,
            double impedanceChange, double previousImpedance, double currentImpedance, string direction)
            : base(batteryId, testId, socPercent, $"Impedance jump detected: ΔZ={impedanceChange:F3}Ω ({direction})")
        {
            ImpedanceChange = impedanceChange;
            PreviousImpedance = previousImpedance;
            CurrentImpedance = currentImpedance;
            Direction = direction;
            AlertType = "ImpedanceJump";
            Value = Math.Abs(impedanceChange);
        }
    }

    public class OutOfBandEventArgs : BatteryEventArgs
    {
        public double ExpectedValue { get; set; }
        public double ActualValue { get; set; }
        public double RunningMean { get; set; }
        public string Parameter { get; set; }

        public OutOfBandEventArgs(string batteryId, string testId, int socPercent,
            string parameter, double actualValue, double expectedValue, double runningMean, string direction)
            : base(batteryId, testId, socPercent, $"{parameter} out of band: {actualValue:F3} {direction} expected range (Mean: {runningMean:F3})")
        {
            Parameter = parameter;
            ActualValue = actualValue;
            ExpectedValue = expectedValue;
            RunningMean = runningMean;
            AlertType = "OutOfBandWarning";
            Value = actualValue;
        }
    }

    
    public class TemperatureSpikeEventArgs : BatteryEventArgs
    {
        public double TemperatureChange { get; set; }
        public double PreviousTemperature { get; set; }
        public double CurrentTemperature { get; set; }
        public string Direction { get; set; }
        public double FrequencyHz { get; set; }

        public TemperatureSpikeEventArgs(string batteryId, string testId, int socPercent,
            double temperatureChange, double previousTemperature, double currentTemperature,
            string direction, double frequencyHz)
            : base(batteryId, testId, socPercent,
                $"Temperature spike detected: ΔT={temperatureChange:F3}°C ({direction}) at F={frequencyHz:F3}Hz, SoC={socPercent}%")
        {
            TemperatureChange = temperatureChange;
            PreviousTemperature = previousTemperature;
            CurrentTemperature = currentTemperature;
            Direction = direction;
            FrequencyHz = frequencyHz;
            AlertType = "TemperatureSpike";
            Value = Math.Abs(temperatureChange);
        }
    }

    
    public class SensorValidationEventArgs : BatteryEventArgs
    {
        public double ActualValue { get; set; }
        public double MinValue { get; set; }
        public double MaxValue { get; set; }
        public string Parameter { get; set; }
        public int SampleIndex { get; set; }

        public SensorValidationEventArgs(string batteryId, string testId, int socPercent,
            string parameter, double actualValue, double minValue, double maxValue, int sampleIndex)
            : base(batteryId, testId, socPercent,
                $"Sensor validation failed: {parameter}={actualValue:F3} not in range [{minValue:F3}, {maxValue:F3}] for sample #{sampleIndex}")
        {
            Parameter = parameter;
            ActualValue = actualValue;
            MinValue = minValue;
            MaxValue = maxValue;
            SampleIndex = sampleIndex;
            AlertType = parameter == "R_ohm" ? "ResistanceOutOfBounds" : "RangeMismatch";
            Value = actualValue;
        }
    }

   
    public delegate void TransferStartedHandler(object sender, BatteryEventArgs e);
    public delegate void SampleReceivedHandler(object sender, BatteryEventArgs e);
    public delegate void TransferCompletedHandler(object sender, BatteryEventArgs e);
    public delegate void WarningRaisedHandler(object sender, BatteryEventArgs e);
    public delegate void VoltageSpikeDHandler(object sender, VoltageEventArgs e);
    public delegate void ImpedanceJumpHandler(object sender, ImpedanceEventArgs e);
    public delegate void OutOfBandWarningHandler(object sender, OutOfBandEventArgs e);
    public delegate void TemperatureSpikeHandler(object sender, TemperatureSpikeEventArgs e);
    public delegate void SensorValidationHandler(object sender, SensorValidationEventArgs e);
}
