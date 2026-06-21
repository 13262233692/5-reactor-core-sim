using System;
using System.Collections.Generic;

namespace ReactorCoreSim.Scripts.Core
{
    public interface ISignalFilter
    {
        double Filter(double input);
        double CurrentValue { get; }
        void Reset(double initialValue);
    }

    public class FirstOrderLowPassFilter : ISignalFilter
    {
        private readonly double _timeConstant;
        private double _currentValue;
        private double _lastTime;
        private readonly bool _useDt;

        public double CurrentValue => _currentValue;

        public FirstOrderLowPassFilter(double timeConstant, double initialValue = 0.0)
        {
            _timeConstant = Math.Max(1e-6, timeConstant);
            _currentValue = initialValue;
            _lastTime = 0.0;
            _useDt = false;
        }

        public FirstOrderLowPassFilter(double timeConstant, double initialValue, bool useDt)
        {
            _timeConstant = Math.Max(1e-6, timeConstant);
            _currentValue = initialValue;
            _lastTime = 0.0;
            _useDt = useDt;
        }

        public double Filter(double input)
        {
            double alpha = 1.0 - Math.Exp(-1.0 / _timeConstant);
            _currentValue += alpha * (input - _currentValue);
            return _currentValue;
        }

        public double Filter(double input, double dt)
        {
            double alpha = 1.0 - Math.Exp(-dt / _timeConstant);
            _currentValue += alpha * (input - _currentValue);
            return _currentValue;
        }

        public void Reset(double initialValue)
        {
            _currentValue = initialValue;
            _lastTime = 0.0;
        }
    }

    public class MovingAverageFilter : ISignalFilter
    {
        private readonly int _windowSize;
        private readonly Queue<double> _window;
        private double _sum;

        public double CurrentValue { get; private set; }

        public MovingAverageFilter(int windowSize, double initialValue = 0.0)
        {
            _windowSize = Math.Max(1, windowSize);
            _window = new Queue<double>(_windowSize);
            _sum = 0.0;
            CurrentValue = initialValue;
            for (int i = 0; i < _windowSize; i++)
            {
                _window.Enqueue(initialValue);
                _sum += initialValue;
            }
        }

        public double Filter(double input)
        {
            double oldest = _window.Dequeue();
            _sum -= oldest;
            _window.Enqueue(input);
            _sum += input;
            CurrentValue = _sum / _windowSize;
            return CurrentValue;
        }

        public void Reset(double initialValue)
        {
            _window.Clear();
            _sum = 0.0;
            for (int i = 0; i < _windowSize; i++)
            {
                _window.Enqueue(initialValue);
                _sum += initialValue;
            }
            CurrentValue = initialValue;
        }
    }

    public class KalmanFilter1D : ISignalFilter
    {
        private double _x;
        private double _p;
        private readonly double _q;
        private readonly double _r;
        private double _k;

        public double CurrentValue => _x;

        public double ProcessNoise
        {
            get => _q;
            init => _q = value;
        }

        public double MeasurementNoise
        {
            get => _r;
            init => _r = value;
        }

        public KalmanFilter1D(double processNoise, double measurementNoise,
            double initialValue = 0.0, double initialError = 1.0)
        {
            _q = processNoise;
            _r = measurementNoise;
            _x = initialValue;
            _p = initialError;
        }

        public double Filter(double input)
        {
            _p += _q;
            _k = _p / (_p + _r);
            _x += _k * (input - _x);
            _p = (1.0 - _k) * _p;
            return _x;
        }

        public void Reset(double initialValue)
        {
            _x = initialValue;
            _p = 1.0;
        }
    }

    public class CascadedFilter : ISignalFilter
    {
        private readonly ISignalFilter[] _filters;

        public double CurrentValue => _filters[^1].CurrentValue;

        public CascadedFilter(params ISignalFilter[] filters)
        {
            _filters = filters;
        }

        public double Filter(double input)
        {
            double value = input;
            foreach (var filter in _filters)
            {
                value = filter.Filter(value);
            }
            return value;
        }

        public void Reset(double initialValue)
        {
            foreach (var filter in _filters)
            {
                filter.Reset(initialValue);
            }
        }
    }

    public class MedianFilter : ISignalFilter
    {
        private readonly int _windowSize;
        private readonly Queue<double> _window;
        private readonly List<double> _sorted;

        public double CurrentValue { get; private set; }

        public MedianFilter(int windowSize, double initialValue = 0.0)
        {
            _windowSize = Math.Max(1, windowSize);
            _window = new Queue<double>(_windowSize);
            _sorted = new List<double>(_windowSize);
            CurrentValue = initialValue;
            for (int i = 0; i < _windowSize; i++)
            {
                _window.Enqueue(initialValue);
                _sorted.Add(initialValue);
            }
            _sorted.Sort();
        }

        public double Filter(double input)
        {
            double oldest = _window.Dequeue();
            _window.Enqueue(input);

            int removeIndex = _sorted.BinarySearch(oldest);
            if (removeIndex >= 0)
            {
                _sorted.RemoveAt(removeIndex);
            }

            int insertIndex = _sorted.BinarySearch(input);
            if (insertIndex < 0)
            {
                insertIndex = ~insertIndex;
            }
            _sorted.Insert(insertIndex, input);

            int mid = _windowSize / 2;
            if (_windowSize % 2 == 0)
            {
                CurrentValue = (_sorted[mid - 1] + _sorted[mid]) * 0.5;
            }
            else
            {
                CurrentValue = _sorted[mid];
            }

            return CurrentValue;
        }

        public void Reset(double initialValue)
        {
            _window.Clear();
            _sorted.Clear();
            for (int i = 0; i < _windowSize; i++)
            {
                _window.Enqueue(initialValue);
                _sorted.Add(initialValue);
            }
            CurrentValue = initialValue;
        }
    }

    public class FlowNoiseGenerator
    {
        private readonly Random _random;
        private readonly double _baseFlow;
        private readonly double _noiseAmplitude;
        private readonly double _driftAmplitude;
        private double _driftPhase;
        private readonly double _driftFrequency;
        private readonly double _vibrationAmplitude;
        private readonly double _vibrationFrequency;
        private double _vibrationPhase;

        public FlowNoiseGenerator(double baseFlow, int seed = 42)
        {
            _random = new Random(seed);
            _baseFlow = baseFlow;
            _noiseAmplitude = baseFlow * 0.005;
            _driftAmplitude = baseFlow * 0.02;
            _driftPhase = 0.0;
            _driftFrequency = 0.1;
            _vibrationAmplitude = baseFlow * 0.003;
            _vibrationFrequency = 10.0;
            _vibrationPhase = 0.0;
        }

        public double GetFlow(double dt)
        {
            double gaussianNoise = GaussianRandom() * _noiseAmplitude;
            _driftPhase += _driftFrequency * dt;
            double drift = Math.Sin(_driftPhase) * _driftAmplitude;
            _vibrationPhase += _vibrationFrequency * dt;
            double vibration = Math.Sin(_vibrationPhase) * _vibrationAmplitude;
            return _baseFlow + gaussianNoise + drift + vibration;
        }

        private double GaussianRandom()
        {
            double u1 = 1.0 - _random.NextDouble();
            double u2 = 1.0 - _random.NextDouble();
            return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        }

        public void Reset()
        {
            _driftPhase = 0.0;
            _vibrationPhase = 0.0;
        }
    }
}
