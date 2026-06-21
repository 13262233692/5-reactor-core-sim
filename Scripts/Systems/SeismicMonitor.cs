using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using Godot;

namespace ReactorCoreSim.Scripts.Systems
{
    public readonly struct SeismicReading
    {
        public double Ax { get; init; }
        public double Ay { get; init; }
        public double Az { get; init; }
        public double Timestamp { get; init; }
        public double Magnitude { get; init; }

        public override string ToString()
        {
            return $"[{Timestamp:F3}s] a=({Ax:F4}, {Ay:F4}, {Az:F4})g |M|={Magnitude:F4}g";
        }
    }

    public enum SeismicEventLevel
    {
        None = 0,
        Notice = 1,
        Alert = 2,
        Trip = 3,
        SafeShutdown = 4
    }

    public class SeismicMonitor
    {
        private readonly ConcurrentQueue<byte> _hexBuffer = new();
        private readonly ConcurrentQueue<SeismicReading> _readingQueue = new();
        private readonly object _stateLock = new();

        private volatile bool _running;
        private Thread? _sniffThread;
        private double _simulationTime;

        private const double TripThreshold = 0.15;
        private const double AlertThreshold = 0.08;
        private const double NoticeThreshold = 0.04;
        private const double MaxGForce = 10.0;

        private const int ReadingWindowSize = 50;
        private readonly List<SeismicReading> _readingWindow = new(ReadingWindowSize);

        private double _peakMagnitude;
        private double _sustainedDuration;
        private SeismicEventLevel _currentLevel;
        private bool _tripTriggered;
        private double _tripTimestamp;

        private double _baselineX;
        private double _baselineY;
        private double _baselineZ;
        private int _baselineSamples;
        private bool _baselineCalibrated;

        private readonly Random _noiseRng = new(12345);
        private bool _injectSimulatedSeismic;
        private double _simulatedMagnitude;
        private double _simulatedDuration;
        private double _simulatedElapsed;
        private double _simulatedPhase;

        public event Action<SeismicReading, SeismicEventLevel>? OnSeismicReading;
        public event Action<SeismicReading>? OnSeismicTrip;

        public SeismicEventLevel CurrentLevel
        {
            get
            {
                lock (_stateLock) return _currentLevel;
            }
        }

        public bool TripTriggered
        {
            get
            {
                lock (_stateLock) return _tripTriggered;
            }
        }

        public double PeakMagnitude
        {
            get
            {
                lock (_stateLock) return _peakMagnitude;
            }
        }

        public double TripTimestamp
        {
            get
            {
                lock (_stateLock) return _tripTimestamp;
            }
        }

        public bool BaselineCalibrated => _baselineCalibrated;

        public SeismicMonitor()
        {
            _running = false;
            _peakMagnitude = 0.0;
            _sustainedDuration = 0.0;
            _currentLevel = SeismicEventLevel.None;
            _tripTriggered = false;
            _tripTimestamp = 0.0;
            _baselineX = 0.0;
            _baselineY = 0.0;
            _baselineZ = 0.0;
            _baselineSamples = 0;
            _baselineCalibrated = false;
            _injectSimulatedSeismic = false;
            _simulatedMagnitude = 0.0;
            _simulatedDuration = 0.0;
            _simulatedElapsed = 0.0;
            _simulatedPhase = 0.0;
        }

        public void Start()
        {
            if (_running) return;
            _running = true;

            _sniffThread = new Thread(SniffHexStreamLoop)
            {
                Name = "SeismicSniffer",
                IsBackground = true,
                Priority = ThreadPriority.AboveNormal
            };
            _sniffThread.Start();
        }

        public void Stop()
        {
            _running = false;
            _sniffThread?.Join(500);
        }

        public void InjectHexData(string hexString)
        {
            if (string.IsNullOrEmpty(hexString)) return;

            int len = hexString.Length;
            for (int i = 0; i < len; i += 2)
            {
                if (i + 1 >= len) break;
                byte b = byte.Parse(hexString.AsSpan(i, 2), NumberStyles.HexNumber);
                _hexBuffer.Enqueue(b);
            }
        }

        public void InjectHexBytes(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return;
            foreach (byte b in bytes)
            {
                _hexBuffer.Enqueue(b);
            }
        }

        public void TriggerSimulatedEarthquake(double magnitudeG, double durationSec)
        {
            lock (_stateLock)
            {
                _injectSimulatedSeismic = true;
                _simulatedMagnitude = Math.Clamp(magnitudeG, 0.0, 5.0);
                _simulatedDuration = Math.Max(0.1, durationSec);
                _simulatedElapsed = 0.0;
                _simulatedPhase = 0.0;
            }
        }

        public void Update(double dt, double simTime)
        {
            _simulationTime = simTime;

            if (_injectSimulatedSeismic)
            {
                GenerateSimulatedReadings(dt);
            }

            ProcessQueuedReadings(dt);
        }

        private void GenerateSimulatedReadings(double dt)
        {
            lock (_stateLock)
            {
                if (!_injectSimulatedSeismic) return;

                _simulatedElapsed += dt;
                _simulatedPhase += dt * 8.0;

                double envelope;
                double halfDur = _simulatedDuration * 0.5;
                if (_simulatedElapsed < halfDur)
                {
                    envelope = _simulatedElapsed / halfDur;
                }
                else
                {
                    envelope = Math.Max(0.0, 1.0 - (_simulatedElapsed - halfDur) / halfDur);
                }

                double mag = _simulatedMagnitude * envelope;

                double ax = mag * (0.6 * Math.Sin(_simulatedPhase) +
                                  0.3 * Math.Sin(_simulatedPhase * 2.7) +
                                  0.1 * (_noiseRng.NextDouble() * 2 - 1));
                double ay = mag * (0.5 * Math.Sin(_simulatedPhase + 1.3) +
                                  0.35 * Math.Cos(_simulatedPhase * 1.9) +
                                  0.15 * (_noiseRng.NextDouble() * 2 - 1));
                double az = mag * (0.4 * Math.Sin(_simulatedPhase + 2.1) +
                                  0.3 * Math.Cos(_simulatedPhase * 2.3) +
                                  0.3 * (_noiseRng.NextDouble() * 2 - 1));

                if (!_baselineCalibrated)
                {
                    ax += _noiseRng.NextDouble() * 0.002 - 0.001;
                    ay += _noiseRng.NextDouble() * 0.002 - 0.001;
                    az += _noiseRng.NextDouble() * 0.002 - 0.001;
                }

                var reading = CreateReading(ax, ay, az);
                _readingQueue.Enqueue(reading);

                if (_simulatedElapsed >= _simulatedDuration)
                {
                    _injectSimulatedSeismic = false;
                }
            }
        }

        private void SniffHexStreamLoop()
        {
            var frameBuffer = new List<byte>(24);

            while (_running)
            {
                bool hasData = false;
                while (_hexBuffer.TryDequeue(out byte b))
                {
                    frameBuffer.Add(b);
                    hasData = true;

                    if (frameBuffer.Count >= 24)
                    {
                        ParseHexFrame(frameBuffer);
                        frameBuffer.Clear();
                    }
                }

                if (!hasData)
                {
                    Thread.Sleep(2);
                }
            }
        }

        private void ParseHexFrame(List<byte> frame)
        {
            if (frame.Count < 24) return;

            try
            {
                double ax = DecodeAxis(frame, 0) / 16384.0;
                double ay = DecodeAxis(frame, 8) / 16384.0;
                double az = DecodeAxis(frame, 16) / 16384.0;

                ax = Math.Clamp(ax, -MaxGForce, MaxGForce);
                ay = Math.Clamp(ay, -MaxGForce, MaxGForce);
                az = Math.Clamp(az, -MaxGForce, MaxGForce);

                if (!_baselineCalibrated)
                {
                    _baselineX += ax;
                    _baselineY += ay;
                    _baselineZ += az;
                    _baselineSamples++;
                    if (_baselineSamples >= 500)
                    {
                        _baselineX /= _baselineSamples;
                        _baselineY /= _baselineSamples;
                        _baselineZ /= _baselineSamples;
                        _baselineCalibrated = true;
                    }
                    return;
                }

                ax -= _baselineX;
                ay -= _baselineY;
                az -= (_baselineZ - 1.0);

                var reading = CreateReading(ax, ay, az);
                _readingQueue.Enqueue(reading);
            }
            catch
            {
            }
        }

        private static double DecodeAxis(List<byte> frame, int offset)
        {
            if (offset + 8 > frame.Count) return 0.0;

            long raw = 0;
            for (int i = 0; i < 8; i++)
            {
                raw = (raw << 8) | frame[offset + i];
            }

            if ((raw & 0x8000000000000000L) != 0)
            {
                raw |= unchecked((long)0xFF00000000000000L);
            }

            return raw / 10000.0;
        }

        private SeismicReading CreateReading(double ax, double ay, double az)
        {
            double magnitude = SpatialPerturbationEvaluator.CalculateVectorMagnitude(ax, ay, az);
            return new SeismicReading
            {
                Ax = ax,
                Ay = ay,
                Az = az,
                Timestamp = _simulationTime,
                Magnitude = magnitude
            };
        }

        private void ProcessQueuedReadings(double dt)
        {
            while (_readingQueue.TryDequeue(out var reading))
            {
                EvaluateReading(reading, dt);
            }
        }

        private void EvaluateReading(SeismicReading reading, double dt)
        {
            lock (_stateLock)
            {
                _readingWindow.Add(reading);
                if (_readingWindow.Count > ReadingWindowSize)
                {
                    _readingWindow.RemoveAt(0);
                }

                double sustainedMag = SpatialPerturbationEvaluator.CalculateSustainedMagnitude(_readingWindow);

                if (reading.Magnitude > _peakMagnitude)
                {
                    _peakMagnitude = reading.Magnitude;
                }

                SeismicEventLevel newLevel;

                if (sustainedMag >= TripThreshold || reading.Magnitude >= TripThreshold * 1.5)
                {
                    newLevel = SeismicEventLevel.Trip;
                    _sustainedDuration += dt;

                    if (!_tripTriggered && _sustainedDuration >= 0.1)
                    {
                        _tripTriggered = true;
                        _tripTimestamp = reading.Timestamp;
                        _currentLevel = SeismicEventLevel.SafeShutdown;

                        ThreadPool.QueueUserWorkItem(_ =>
                        {
                            try { OnSeismicTrip?.Invoke(reading); }
                            catch { }
                        });
                    }
                }
                else if (sustainedMag >= AlertThreshold)
                {
                    newLevel = SeismicEventLevel.Alert;
                    _sustainedDuration = Math.Max(0, _sustainedDuration - dt * 0.5);
                }
                else if (sustainedMag >= NoticeThreshold)
                {
                    newLevel = SeismicEventLevel.Notice;
                    _sustainedDuration = Math.Max(0, _sustainedDuration - dt);
                }
                else
                {
                    newLevel = SeismicEventLevel.None;
                    _sustainedDuration = Math.Max(0, _sustainedDuration - dt * 2);
                    if (_sustainedDuration <= 0 && !_tripTriggered)
                    {
                        _peakMagnitude *= 0.99;
                    }
                }

                if (!_tripTriggered)
                {
                    _currentLevel = newLevel;
                }

                try { OnSeismicReading?.Invoke(reading, _currentLevel); }
                catch { }
            }
        }

        public void Reset()
        {
            lock (_stateLock)
            {
                _peakMagnitude = 0.0;
                _sustainedDuration = 0.0;
                _currentLevel = SeismicEventLevel.None;
                _tripTriggered = false;
                _tripTimestamp = 0.0;
                _readingWindow.Clear();
                _injectSimulatedSeismic = false;
            }
        }
    }

    public static class SpatialPerturbationEvaluator
    {
        public static double CalculateVectorMagnitude(double ax, double ay, double az)
        {
            double sumSq = ax * ax + ay * ay + az * az;

            if (double.IsNaN(sumSq) || double.IsInfinity(sumSq))
            {
                return 0.0;
            }

            if (sumSq < 0) sumSq = 0.0;

            return Math.Sqrt(sumSq);
        }

        public static double CalculateSustainedMagnitude(List<SeismicReading> window)
        {
            if (window == null || window.Count == 0) return 0.0;

            double sum = 0.0;
            double peak = 0.0;
            int validCount = 0;

            int n = window.Count;
            for (int i = 0; i < n; i++)
            {
                double mag = window[i].Magnitude;
                if (double.IsNaN(mag) || double.IsInfinity(mag) || mag < 0)
                {
                    continue;
                }

                double weight = (i + 1.0) / n;
                sum += mag * weight;
                if (mag > peak) peak = mag;
                validCount++;
            }

            if (validCount == 0) return 0.0;

            double avgWeight = (validCount + 1.0) / (2.0 * validCount);
            double weightedAvg = sum / (validCount * avgWeight);

            double sustained = 0.6 * weightedAvg + 0.4 * peak;

            if (double.IsNaN(sustained) || double.IsInfinity(sustained))
            {
                sustained = peak;
            }

            return Math.Max(0.0, sustained);
        }

        public static bool IsTripCondition(double magnitude, double threshold = 0.15)
        {
            if (double.IsNaN(magnitude) || double.IsInfinity(magnitude))
            {
                return false;
            }
            return magnitude >= threshold;
        }

        public static string FormatMagnitude(double g)
        {
            if (double.IsNaN(g) || double.IsInfinity(g)) return "--";
            if (g < 0.001) return "< 0.001g";
            return $"{g:F3}g";
        }
    }
}
