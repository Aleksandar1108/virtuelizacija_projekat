using Common;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Globalization;
using System.IO;
using System.ServiceModel;

namespace Server
{
    [ServiceBehavior(InstanceContextMode = InstanceContextMode.Single, ConcurrencyMode = ConcurrencyMode.Single)]
    public class BatteryService : IBatteryService, IDisposable
    {
        private readonly string storageRoot = ConfigurationManager.AppSettings["storagePath"] ?? "BatteryStorage";
        private IBatteryStorage storage;
        private string currentSessionDir;

      
        private EisMeta currentSession;
        private double? lastVoltage;
        private double? lastImpedance;
        private double? lastTemperature;
        private double runningMeanImpedance;
        private long sampleCount;
        private int written;
        private readonly object lockObject = new object();

      
        private double tThreshold;
        private double rMin, rMax;
        private double rangeMin, rangeMax;

     
        public event TransferStartedHandler OnTransferStarted;
        public event SampleReceivedHandler OnSampleReceived;
        public event TransferCompletedHandler OnTransferCompleted;
        public event WarningRaisedHandler OnWarningRaised;
        public event VoltageSpikeDHandler OnVoltageSpike;
        public event ImpedanceJumpHandler OnImpedanceJump;
        public event OutOfBandWarningHandler OnOutOfBandWarning;
        public event TemperatureSpikeHandler OnTemperatureSpike;
        public event SensorValidationHandler OnSensorValidation;

        public BatteryService()
        {
         
            tThreshold = double.Parse(ConfigurationManager.AppSettings["T_threshold"] ?? "2.0", CultureInfo.InvariantCulture);
            rMin = double.Parse(ConfigurationManager.AppSettings["R_min"] ?? "0.001", CultureInfo.InvariantCulture);
            rMax = double.Parse(ConfigurationManager.AppSettings["R_max"] ?? "1000.0", CultureInfo.InvariantCulture);
            rangeMin = double.Parse(ConfigurationManager.AppSettings["Range_min"] ?? "0.1", CultureInfo.InvariantCulture);
            rangeMax = double.Parse(ConfigurationManager.AppSettings["Range_max"] ?? "10000.0", CultureInfo.InvariantCulture);

            PrintWelcomeBanner();

           
            OnTransferStarted += (s, e) =>
            {
                Console.WriteLine();
                Console.WriteLine("SESSION STARTED");
                Console.WriteLine($"Battery: {e.BatteryId} | Test: {e.TestId} | SoC: {e.SocPercent}%");
                Console.WriteLine($"{e.Message}");
                Console.WriteLine($"Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                Console.WriteLine();
            };

            OnSampleReceived += (s, e) =>
            {
                if (written % 50 == 0 && written > 0)
                {
                    Console.WriteLine($"\nProcessed {written} samples...");
                }
                else
                {
                    Console.Write(".");
                }
            };

            OnTransferCompleted += (s, e) =>
            {
                Console.WriteLine();
                Console.WriteLine("SESSION COMPLETED");
                Console.WriteLine($"Battery: {e.BatteryId} | Test: {e.TestId} | SoC: {e.SocPercent}%");
                Console.WriteLine($"{e.Message}");
                Console.WriteLine($"Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                Console.WriteLine();
            };

            OnWarningRaised += (s, e) =>
            {
                Console.WriteLine();
                Console.WriteLine("WARNING");
                Console.WriteLine($"Message: {e.Message}");
                Console.WriteLine($"Battery: {e.BatteryId} | SoC: {e.SocPercent}%");
                Console.WriteLine($"Time: {DateTime.Now:HH:mm:ss}");
                Console.WriteLine();
            };

          
            OnVoltageSpike += (s, e) =>
            {
                Console.WriteLine();
                Console.WriteLine("VOLTAGE SPIKE DETECTED");
                Console.WriteLine($"ΔV = {e.VoltageChange:+0.000;-0.000}V ({e.Direction})");
                Console.WriteLine($"Previous: {e.PreviousVoltage:F3}V → Current: {e.CurrentVoltage:F3}V");
                Console.WriteLine($"Threshold: {e.Threshold:F3}V");
                Console.WriteLine($"Battery: {e.BatteryId} | SoC: {e.SocPercent}% | Time: {DateTime.Now:HH:mm:ss}");
                Console.WriteLine();
            };

            OnImpedanceJump += (s, e) =>
            {
                Console.WriteLine();
                Console.WriteLine("IMPEDANCE JUMP DETECTED");
                Console.WriteLine($"ΔZ = {e.ImpedanceChange:+0.000;-0.000}Ω ({e.Direction})");
                Console.WriteLine($"Previous: {e.PreviousImpedance:F3}Ω → Current: {e.CurrentImpedance:F3}Ω");
                Console.WriteLine($"Threshold: {e.Threshold:F3}Ω");
                Console.WriteLine($"Battery: {e.BatteryId} | SoC: {e.SocPercent}% | Time: {DateTime.Now:HH:mm:ss}");
                Console.WriteLine();
            };

            OnOutOfBandWarning += (s, e) =>
            {
                Console.WriteLine();
                Console.WriteLine("OUT OF BAND WARNING");
                Console.WriteLine($"Parameter: {e.Parameter} = {e.ActualValue:F3}");
                Console.WriteLine($"Running Mean: {e.RunningMean:F3}");
                Console.WriteLine($"Expected Range: {e.ExpectedValue:F3}");
                Console.WriteLine($"Battery: {e.BatteryId} | SoC: {e.SocPercent}% | Time: {DateTime.Now:HH:mm:ss}");
                Console.WriteLine();
            };

          
            OnTemperatureSpike += (s, e) =>
            {
                Console.WriteLine();
                Console.WriteLine("TEMPERATURE SPIKE DETECTED - POTENTIAL OVERHEATING");
                Console.WriteLine($"ΔT = {e.TemperatureChange:+0.000;-0.000}°C ({e.Direction})");
                Console.WriteLine($"Previous: {e.PreviousTemperature:F1}°C → Current: {e.CurrentTemperature:F1}°C");
                Console.WriteLine($"Frequency: {e.FrequencyHz:F3}Hz | Threshold: {e.Threshold:F1}°C");
                Console.WriteLine($"Battery: {e.BatteryId} | SoC: {e.SocPercent}% | Time: {DateTime.Now:HH:mm:ss}");
                Console.WriteLine();
            };

         
            OnSensorValidation += (s, e) =>
            {
                Console.WriteLine();
                Console.WriteLine("SENSOR VALIDATION ERROR - SENSOR MALFUNCTION");
                Console.WriteLine($"Parameter: {e.Parameter} = {e.ActualValue:F3}");
                Console.WriteLine($"Valid Range: [{e.MinValue:F3}, {e.MaxValue:F3}]");
                Console.WriteLine($"Sample Index: #{e.SampleIndex}");
                Console.WriteLine($"Battery: {e.BatteryId} | SoC: {e.SocPercent}% | Time: {DateTime.Now:HH:mm:ss}");
                Console.WriteLine();
            };
        }

        private void PrintWelcomeBanner()
        {
            Console.Clear();
            Console.WriteLine("=== Li-ion Battery EIS Analysis Server ===");
            Console.WriteLine("Real-time Analytics & Monitoring");
            Console.WriteLine();

            Console.WriteLine("BatteryService Initialized");
            Console.WriteLine();

            Console.WriteLine("Configuration Thresholds:");
            Console.WriteLine($"  Temperature: ΔT > {tThreshold}°C (Spike detection)");
            Console.WriteLine($"  Resistance: R [{rMin}, {rMax}]Ω (Sensor validation)");
            Console.WriteLine($"  Range: [{rangeMin}, {rangeMax}]Ω (Sensor validation)");
            Console.WriteLine();

            Console.WriteLine("Active Analytics:");
            Console.WriteLine("  - Voltage Spike Detection");
            Console.WriteLine("  - Impedance Jump Detection");
            Console.WriteLine("  - Temperature Spike Detection (OVERHEATING)");
            Console.WriteLine("  - Sensor Validation (R & Range)");
            Console.WriteLine("  - Out-of-Band Warnings");
            Console.WriteLine();

            Console.WriteLine("Server Ready - Waiting for client connections...");
            Console.WriteLine("================================================");
        }

        public Ack StartSession(EisMeta meta)
        {
            lock (lockObject)
            {
                try
                {
                    if (meta == null)
                        throw new FaultException<ValidationFault>(new ValidationFault
                        {
                            Message = "EisMeta is null",
                            Field = "meta",
                            Value = "null"
                        });

                   
                    if (string.IsNullOrWhiteSpace(meta.BatteryId))
                        throw new FaultException<ValidationFault>(new ValidationFault
                        {
                            Message = "BatteryId is required",
                            Field = "BatteryId",
                            Value = meta.BatteryId ?? "null"
                        });

                    if (string.IsNullOrWhiteSpace(meta.TestId))
                        throw new FaultException<ValidationFault>(new ValidationFault
                        {
                            Message = "TestId is required",
                            Field = "TestId",
                            Value = meta.TestId ?? "null"
                        });

                    if (meta.SocPercent < 0 || meta.SocPercent > 100)
                        throw new FaultException<ValidationFault>(new ValidationFault
                        {
                            Message = "SoC% must be between 0 and 100",
                            Field = "SocPercent",
                            Value = meta.SocPercent.ToString()
                        });

                    if (meta.VThreshold <= 0)
                        throw new FaultException<ValidationFault>(new ValidationFault
                        {
                            Message = "V_threshold must be positive",
                            Field = "VThreshold",
                            Value = meta.VThreshold.ToString()
                        });

                    if (meta.ZThreshold <= 0)
                        throw new FaultException<ValidationFault>(new ValidationFault
                        {
                            Message = "Z_threshold must be positive",
                            Field = "ZThreshold",
                            Value = meta.ZThreshold.ToString()
                        });

                    if (meta.DeviationPercent <= 0 || meta.DeviationPercent > 100)
                        throw new FaultException<ValidationFault>(new ValidationFault
                        {
                            Message = "DeviationPercent must be between 0 and 100",
                            Field = "DeviationPercent",
                            Value = meta.DeviationPercent.ToString()
                        });

                    currentSession = meta;

                  
                    currentSessionDir = Path.Combine(storageRoot, meta.BatteryId, meta.TestId, $"{meta.SocPercent}%");
                    Directory.CreateDirectory(currentSessionDir);

                    storage = new BatteryFileStorage(currentSessionDir);
                    storage.InitializeSession(meta);

                 
                    lastVoltage = null;
                    lastImpedance = null;
                    lastTemperature = null;
                    runningMeanImpedance = 0;
                    sampleCount = 0;
                    written = 0;

                    var startEvent = new BatteryEventArgs(meta.BatteryId, meta.TestId, meta.SocPercent,
                        $"File: {meta.FileName} | Expected: {meta.TotalRows} samples");
                    OnTransferStarted?.Invoke(this, startEvent);

                    Console.WriteLine($"Storage Directory: {currentSessionDir}");
                    Console.WriteLine($"Analytics Thresholds: V={meta.VThreshold:F3}V, Z={meta.ZThreshold:F3}Ω, Deviation={meta.DeviationPercent}%");

                    return new Ack { Success = true, Message = "Session started", Status = "IN_PROGRESS" };
                }
                catch (FaultException)
                {
                    throw; 
                }
                catch (Exception ex)
                {
                    throw new FaultException<DataFormatFault>(new DataFormatFault
                    {
                        Message = ex.Message,
                        Details = ex.StackTrace
                    });
                }
            }
        }

        public Ack PushSample(EisSample sample)
        {
            lock (lockObject)
            {
                try
                {
                    if (storage == null || currentSession == null)
                        throw new FaultException<ValidationFault>(new ValidationFault
                        {
                            Message = "Session not started",
                            Field = "session",
                            Value = "null"
                        });

                  
                    if (!ValidateSample(sample, out string validationError))
                    {
                        storage.StoreRejectedSample(validationError, SerializeSample(sample));
                        throw new FaultException<ValidationFault>(new ValidationFault
                        {
                            Message = validationError,
                            Field = "sample",
                            Value = SerializeSample(sample)
                        });
                    }

                    
                    if (sample.R_ohm < rMin || sample.R_ohm > rMax)
                    {
                        var sensorEvent = new SensorValidationEventArgs(currentSession.BatteryId, currentSession.TestId,
                            currentSession.SocPercent, "R_ohm", sample.R_ohm, rMin, rMax, sample.RowIndex);
                        OnSensorValidation?.Invoke(this, sensorEvent);
                        storage.StoreAnalyticsEvent("ResistanceOutOfBounds", sensorEvent.Message, sample.R_ohm, 0);
                        storage.StoreRejectedSample($"R_ohm out of bounds: {sample.R_ohm} not in [{rMin}, {rMax}]", SerializeSample(sample));
                    }

                    if (sample.Range_ohm < rangeMin || sample.Range_ohm > rangeMax)
                    {
                        var sensorEvent = new SensorValidationEventArgs(currentSession.BatteryId, currentSession.TestId,
                            currentSession.SocPercent, "Range_ohm", sample.Range_ohm, rangeMin, rangeMax, sample.RowIndex);
                        OnSensorValidation?.Invoke(this, sensorEvent);
                        storage.StoreAnalyticsEvent("RangeMismatch", sensorEvent.Message, sample.Range_ohm, 0);
                        storage.StoreRejectedSample($"Range_ohm out of bounds: {sample.Range_ohm} not in [{rangeMin}, {rangeMax}]", SerializeSample(sample));
                    }

                 
                    storage.StoreSample(sample);
                    written++;

                    
                    if (lastTemperature.HasValue)
                    {
                        double deltaT = sample.T_degC - lastTemperature.Value;
                        if (Math.Abs(deltaT) > tThreshold)
                        {
                            string direction = deltaT > 0 ? "porast" : "pad";
                            var tempEvent = new TemperatureSpikeEventArgs(currentSession.BatteryId, currentSession.TestId,
                                currentSession.SocPercent, deltaT, lastTemperature.Value, sample.T_degC, direction, sample.FrequencyHz);
                            tempEvent.Threshold = tThreshold;

                            OnTemperatureSpike?.Invoke(this, tempEvent);
                            storage.StoreAnalyticsEvent("TemperatureSpike", tempEvent.Message, Math.Abs(deltaT), tThreshold);
                        }
                    }
                    lastTemperature = sample.T_degC;

                    
                    if (lastVoltage.HasValue)
                    {
                        double deltaV = sample.V - lastVoltage.Value;
                        if (Math.Abs(deltaV) > currentSession.VThreshold)
                        {
                            string direction = deltaV > 0 ? "IZNAD očekivanog" : "ISPOD očekivanog";
                            var voltageEvent = new VoltageEventArgs(currentSession.BatteryId, currentSession.TestId,
                                currentSession.SocPercent, deltaV, lastVoltage.Value, sample.V, direction);
                            voltageEvent.Threshold = currentSession.VThreshold;

                            OnVoltageSpike?.Invoke(this, voltageEvent);
                            storage.StoreAnalyticsEvent("VoltageSpike", voltageEvent.Message, Math.Abs(deltaV), currentSession.VThreshold);
                        }
                    }
                    lastVoltage = sample.V;

                   
                    double currentImpedance = sample.CalculateImpedance();
                    if (lastImpedance.HasValue)
                    {
                        double deltaZ = currentImpedance - lastImpedance.Value;
                        if (Math.Abs(deltaZ) > currentSession.ZThreshold)
                        {
                            string direction = deltaZ > 0 ? "IZNAD očekivanog" : "ISPOD očekivanog";
                            var impedanceEvent = new ImpedanceEventArgs(currentSession.BatteryId, currentSession.TestId,
                                currentSession.SocPercent, deltaZ, lastImpedance.Value, currentImpedance, direction);
                            impedanceEvent.Threshold = currentSession.ZThreshold;

                            OnImpedanceJump?.Invoke(this, impedanceEvent);
                            storage.StoreAnalyticsEvent("ImpedanceJump", impedanceEvent.Message, Math.Abs(deltaZ), currentSession.ZThreshold);
                        }
                    }

                    
                    runningMeanImpedance = ((runningMeanImpedance * sampleCount) + currentImpedance) / (sampleCount + 1);
                    sampleCount++;

                    double lowBound = runningMeanImpedance * (1 - currentSession.DeviationPercent / 100.0);
                    double highBound = runningMeanImpedance * (1 + currentSession.DeviationPercent / 100.0);

                    if (currentImpedance < lowBound)
                    {
                        var outOfBandEvent = new OutOfBandEventArgs(currentSession.BatteryId, currentSession.TestId,
                            currentSession.SocPercent, "Impedance", currentImpedance, lowBound, runningMeanImpedance, "ISPOD očekivane vrednosti");
                        OnOutOfBandWarning?.Invoke(this, outOfBandEvent);
                        storage.StoreAnalyticsEvent("OutOfBandWarning", outOfBandEvent.Message, currentImpedance, lowBound);
                    }
                    else if (currentImpedance > highBound)
                    {
                        var outOfBandEvent = new OutOfBandEventArgs(currentSession.BatteryId, currentSession.TestId,
                            currentSession.SocPercent, "Impedance", currentImpedance, highBound, runningMeanImpedance, "IZNAD očekivane vrednosti");
                        OnOutOfBandWarning?.Invoke(this, outOfBandEvent);
                        storage.StoreAnalyticsEvent("OutOfBandWarning", outOfBandEvent.Message, currentImpedance, highBound);
                    }

                    lastImpedance = currentImpedance;

                    var sampleEvent = new BatteryEventArgs(currentSession.BatteryId, currentSession.TestId,
                        currentSession.SocPercent, "Sample received");
                    OnSampleReceived?.Invoke(this, sampleEvent);

                    if (written <= 3)
                    {
                        Console.WriteLine();
                        Console.WriteLine($"Sample #{written} processed successfully");
                        Console.WriteLine($"V={sample.V:F3}V | Z={currentImpedance:F3}Ω | F={sample.FrequencyHz:F3}Hz | T={sample.T_degC:F1}°C");
                    }
                    if (written % 20 == 0) Console.Write(" ");

                    return new Ack { Success = true, Message = "Sample accepted", Status = "IN_PROGRESS" };
                }
                catch (FaultException)
                {
                    throw; 
                }
                catch (Exception ex)
                {
                    if (storage != null)
                        storage.StoreRejectedSample($"Processing error: {ex.Message}", SerializeSample(sample));

                    throw new FaultException<DataFormatFault>(new DataFormatFault
                    {
                        Message = ex.Message,
                        Details = ex.StackTrace
                    });
                }
            }
        }

        public Ack EndSession()
        {
            lock (lockObject)
            {
                try
                {
                    if (storage == null || currentSession == null)
                        throw new FaultException<ValidationFault>(new ValidationFault
                        {
                            Message = "No active session",
                            Field = "session",
                            Value = "null"
                        });

                    storage.FinalizeSession();

                    var endEvent = new BatteryEventArgs(currentSession.BatteryId, currentSession.TestId,
                        currentSession.SocPercent, $"{written} samples processed successfully");
                    OnTransferCompleted?.Invoke(this, endEvent);

                    Console.WriteLine();
                    Console.WriteLine($"Final Statistics: {written} samples processed");
                    Console.WriteLine($"Data saved to: {currentSessionDir}");

                    Dispose();
                    return new Ack { Success = true, Message = "Session completed", Status = "COMPLETED" };
                }
                catch (FaultException)
                { 
                    throw; 
                }
                catch (Exception ex)
                {
                    throw new FaultException<DataFormatFault>(new DataFormatFault
                    {
                        Message = ex.Message,
                        Details = ex.StackTrace
                    });
                }
            }
        }

        private bool ValidateSample(EisSample sample, out string error)
        {
            error = string.Empty;

            if (sample == null)
            {
                error = "Sample is null";
                return false;
            }

         
            if (sample.FrequencyHz <= 0 || double.IsNaN(sample.FrequencyHz) || double.IsInfinity(sample.FrequencyHz))
            {
                error = $"Invalid FrequencyHz: {sample.FrequencyHz} (must be positive)";
                return false;
            }

            
            if (double.IsNaN(sample.R_ohm) || double.IsInfinity(sample.R_ohm))
            {
                error = $"Invalid R_ohm: {sample.R_ohm}";
                return false;
            }

            if (double.IsNaN(sample.X_ohm) || double.IsInfinity(sample.X_ohm))
            {
                error = $"Invalid X_ohm: {sample.X_ohm}";
                return false;
            }

            if (double.IsNaN(sample.V) || double.IsInfinity(sample.V))
            {
                error = $"Invalid V: {sample.V}";
                return false;
            }

            
            if (sample.RowIndex < 0)
            {
                error = $"Invalid RowIndex: {sample.RowIndex} (must be non-negative)";
                return false;
            }

          
            if (sample.Timestamp == default(DateTime))
            {
                error = "Invalid Timestamp";
                return false;
            }

            return true;
        }

        private static string SerializeSample(EisSample sample)
        {
            if (sample == null) return "<null>";
            return $"{sample.FrequencyHz},{sample.R_ohm},{sample.X_ohm},{sample.V},{sample.T_degC},{sample.Range_ohm},{sample.RowIndex}";
        }

        public void Dispose()
        {
            lock (lockObject)
            {
                try
                {
                    storage?.Dispose();
                    storage = null;
                    currentSession = null;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error during disposal: {ex.Message}");
                }
            }
        }
    }
}