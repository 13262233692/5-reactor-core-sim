using System;

namespace ReactorCoreSim.Scripts.Core
{
    public readonly struct PointKineticsParameters
    {
        public double NeutronGenerationTime { get; init; }
        public double TotalDelayedNeutronFraction { get; init; }
        public double[] DelayedNeutronFractions { get; init; }
        public double[] DecayConstants { get; init; }
        public double ExternalSource { get; init; }
        public double InitialPower { get; init; }

        public static PointKineticsParameters DefaultPwr()
        {
            return new PointKineticsParameters
            {
                NeutronGenerationTime = 1e-4,
                TotalDelayedNeutronFraction = 0.0065,
                DelayedNeutronFractions = new double[]
                {
                    0.00021, 0.00142, 0.00127, 0.00257, 0.00075, 0.00028
                },
                DecayConstants = new double[]
                {
                    0.0127, 0.0317, 0.115, 0.311, 1.40, 3.87
                },
                ExternalSource = 1e6,
                InitialPower = 1e-6
            };
        }
    }

    public struct PointKineticsState
    {
        public double NeutronDensity;
        public double[] PrecursorConcentrations;
        public double Time;
        public double Reactivity;

        public PointKineticsState(int numPrecursorGroups)
        {
            NeutronDensity = 0.0;
            PrecursorConcentrations = new double[numPrecursorGroups];
            Time = 0.0;
            Reactivity = 0.0;
        }
    }

    public class PointKineticsSolver
    {
        private readonly PointKineticsParameters _parameters;
        private PointKineticsState _state;
        private readonly int _numGroups;
        private readonly double[] _k1N;
        private readonly double[] _k2N;
        private readonly double[] _k3N;
        private readonly double[] _k4N;
        private readonly double[][] _k1C;
        private readonly double[][] _k2C;
        private readonly double[][] _k3C;
        private readonly double[][] _k4C;

        public PointKineticsState State => _state;
        public double Power => _state.NeutronDensity;

        public PointKineticsSolver(PointKineticsParameters parameters)
        {
            _parameters = parameters;
            _numGroups = parameters.DelayedNeutronFractions.Length;
            _state = new PointKineticsState(_numGroups);
            _k1N = new double[_numGroups];
            _k2N = new double[_numGroups];
            _k3N = new double[_numGroups];
            _k4N = new double[_numGroups];
            _k1C = new double[_numGroups][];
            _k2C = new double[_numGroups][];
            _k3C = new double[_numGroups][];
            _k4C = new double[_numGroups][];
            for (int i = 0; i < _numGroups; i++)
            {
                _k1C[i] = new double[1];
                _k2C[i] = new double[1];
                _k3C[i] = new double[1];
                _k4C[i] = new double[1];
            }
            InitializeSteadyState();
        }

        private void InitializeSteadyState()
        {
            _state.NeutronDensity = _parameters.InitialPower;
            _state.Time = 0.0;
            _state.Reactivity = 0.0;

            for (int i = 0; i < _numGroups; i++)
            {
                _state.PrecursorConcentrations[i] =
                    (_parameters.DelayedNeutronFractions[i] /
                     (_parameters.DecayConstants[i] * _parameters.NeutronGenerationTime)) *
                    _parameters.InitialPower;
            }
        }

        public void SetReactivity(double reactivity)
        {
            _state.Reactivity = reactivity;
        }

        public void Step(double dt)
        {
            double rho = _state.Reactivity;
            double beta = _parameters.TotalDelayedNeutronFraction;
            double lambda = _parameters.NeutronGenerationTime;
            double source = _parameters.ExternalSource;
            double[] betas = _parameters.DelayedNeutronFractions;
            double[] lambdas = _parameters.DecayConstants;
            double n0 = _state.NeutronDensity;
            double[] c0 = _state.PrecursorConcentrations;

            double k1_n = DnDt(rho, beta, lambda, n0, c0, lambdas, source);
            for (int i = 0; i < _numGroups; i++)
            {
                _k1C[i][0] = DcDt(i, betas[i], lambda, n0, lambdas[i], c0[i]);
            }

            double n1 = n0 + 0.5 * dt * k1_n;
            double[] c1 = new double[_numGroups];
            for (int i = 0; i < _numGroups; i++)
            {
                c1[i] = c0[i] + 0.5 * dt * _k1C[i][0];
            }

            double k2_n = DnDt(rho, beta, lambda, n1, c1, lambdas, source);
            for (int i = 0; i < _numGroups; i++)
            {
                _k2C[i][0] = DcDt(i, betas[i], lambda, n1, lambdas[i], c1[i]);
            }

            double n2 = n0 + 0.5 * dt * k2_n;
            double[] c2 = new double[_numGroups];
            for (int i = 0; i < _numGroups; i++)
            {
                c2[i] = c0[i] + 0.5 * dt * _k2C[i][0];
            }

            double k3_n = DnDt(rho, beta, lambda, n2, c2, lambdas, source);
            for (int i = 0; i < _numGroups; i++)
            {
                _k3C[i][0] = DcDt(i, betas[i], lambda, n2, lambdas[i], c2[i]);
            }

            double n3 = n0 + dt * k3_n;
            double[] c3 = new double[_numGroups];
            for (int i = 0; i < _numGroups; i++)
            {
                c3[i] = c0[i] + dt * _k3C[i][0];
            }

            double k4_n = DnDt(rho, beta, lambda, n3, c3, lambdas, source);
            for (int i = 0; i < _numGroups; i++)
            {
                _k4C[i][0] = DcDt(i, betas[i], lambda, n3, lambdas[i], c3[i]);
            }

            _state.NeutronDensity = n0 + (dt / 6.0) * (k1_n + 2 * k2_n + 2 * k3_n + k4_n);

            for (int i = 0; i < _numGroups; i++)
            {
                _state.PrecursorConcentrations[i] = c0[i] +
                    (dt / 6.0) * (_k1C[i][0] + 2 * _k2C[i][0] + 2 * _k3C[i][0] + _k4C[i][0]);
            }

            _state.Time += dt;

            if (_state.NeutronDensity < 0) _state.NeutronDensity = 1e-20;
            for (int i = 0; i < _numGroups; i++)
            {
                if (_state.PrecursorConcentrations[i] < 0)
                    _state.PrecursorConcentrations[i] = 0;
            }
        }

        private static double DnDt(double rho, double beta, double lambda,
            double n, double[] c, double[] lambdas, double source)
        {
            double sum = 0.0;
            for (int i = 0; i < lambdas.Length; i++)
            {
                sum += lambdas[i] * c[i];
            }
            return ((rho - beta) / lambda) * n + sum + source;
        }

        private static double DcDt(int i, double beta_i, double lambda,
            double n, double lambda_i, double c_i)
        {
            return (beta_i / lambda) * n - lambda_i * c_i;
        }

        public double GetTotalPrecursorConcentration()
        {
            double sum = 0.0;
            for (int i = 0; i < _numGroups; i++)
            {
                sum += _state.PrecursorConcentrations[i];
            }
            return sum;
        }

        public double GetDoublingTime()
        {
            if (_state.Reactivity <= 0 || _state.Reactivity <= _parameters.TotalDelayedNeutronFraction)
            {
                return double.PositiveInfinity;
            }
            double rho = _state.Reactivity - _parameters.TotalDelayedNeutronFraction;
            return Math.Log(2) * _parameters.NeutronGenerationTime / rho;
        }
    }
}
