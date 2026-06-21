using System;

namespace ReactorCoreSim.Scripts.Core
{
    public readonly struct XenonKineticsParameters
    {
        public double Iodine135HalfLife { get; init; }
        public double Xenon135HalfLife { get; init; }
        public double IodineYield { get; init; }
        public double XenonYield { get; init; }
        public double XenonMicroscopicAbsorptionCrossSection { get; init; }
        public double ThermalNeutronFluxNominal { get; init; }
        public double MacroscopicFissionCrossSection { get; init; }
        public double EnergyPerFission { get; init; }

        public static XenonKineticsParameters DefaultPwr()
        {
            return new XenonKineticsParameters
            {
                Iodine135HalfLife = 6.7 * 3600.0,
                Xenon135HalfLife = 9.2 * 3600.0,
                IodineYield = 0.061,
                XenonYield = 0.003,
                XenonMicroscopicAbsorptionCrossSection = 2.65e-18,
                ThermalNeutronFluxNominal = 3.0e17,
                MacroscopicFissionCrossSection = 0.33,
                EnergyPerFission = 3.204e-11
            };
        }
    }

    public struct XenonKineticsState
    {
        public double IodineConcentration;
        public double XenonConcentration;
        public double IodineDensity;
        public double XenonDensity;
        public double XenonReactivityWorth;
        public double EquilibriumXenon;
        public double Time;
        public bool IsPostShutdown;
        public double TimeSinceShutdown;
    }

    public class XenonKineticsSolver
    {
        private readonly XenonKineticsParameters _parameters;
        private XenonKineticsState _state;

        private readonly double _lambdaI;
        private readonly double _lambdaXe;
        private readonly double _gammaI;
        private readonly double _gammaXe;
        private readonly double _sigmaXea;
        private readonly double _fluxNominal;

        private const double MinConcentration = 1e-30;
        private const double MaxConcentration = 1e30;
        private const double MinStepRatio = 0.001;

        public XenonKineticsState State => _state;
        public double XenonReactivity => _state.XenonReactivityWorth;

        public XenonKineticsSolver(XenonKineticsParameters parameters)
        {
            _parameters = parameters;
            _lambdaI = Math.Log(2.0) / parameters.Iodine135HalfLife;
            _lambdaXe = Math.Log(2.0) / parameters.Xenon135HalfLife;
            _gammaI = parameters.IodineYield;
            _gammaXe = parameters.XenonYield;
            _sigmaXea = parameters.XenonMicroscopicAbsorptionCrossSection;
            _fluxNominal = parameters.ThermalNeutronFluxNominal;
            _state = new XenonKineticsState();
            InitializeEquilibrium();
        }

        private void InitializeEquilibrium()
        {
            double flux = _fluxNominal;
            double sigmaF = _parameters.MacroscopicFissionCrossSection;

            _state.IodineConcentration = (_gammaI * sigmaF * flux) / _lambdaI;
            _state.XenonConcentration =
                ((_gammaI + _gammaXe) * sigmaF * flux) /
                (_lambdaXe + _sigmaXea * flux);

            _state.IodineDensity = _state.IodineConcentration;
            _state.XenonDensity = _state.XenonConcentration;
            _state.EquilibriumXenon = _state.XenonConcentration;

            _state.XenonReactivityWorth = CalculateXenonReactivity(_state.XenonConcentration);
            _state.Time = 0.0;
            _state.IsPostShutdown = false;
            _state.TimeSinceShutdown = 0.0;
        }

        public void Step(double dt, double currentPowerFraction)
        {
            if (dt <= 0.0) return;

            double flux = _fluxNominal * Math.Max(0.0, currentPowerFraction);
            double sigmaF = _parameters.MacroscopicFissionCrossSection;

            double maxDt = CalculateMaxStableDt(flux);
            double remaining = dt;
            int subSteps = 0;
            const int MaxSubSteps = 1000000;

            while (remaining > 0.0 && subSteps < MaxSubSteps)
            {
                double subDt = Math.Min(remaining, maxDt);
                subDt = Math.Max(subDt, 1e-6);

                EulerStepWithClamp(subDt, flux, sigmaF);

                remaining -= subDt;
                subSteps++;

                if (double.IsNaN(_state.IodineConcentration) ||
                    double.IsNaN(_state.XenonConcentration))
                {
                    InitializeEquilibrium();
                    return;
                }
            }

            _state.Time += dt;

            if (currentPowerFraction < 0.01)
            {
                _state.IsPostShutdown = true;
                _state.TimeSinceShutdown += dt;
            }
            else
            {
                _state.IsPostShutdown = false;
                _state.TimeSinceShutdown = 0.0;
            }

            _state.XenonReactivityWorth = CalculateXenonReactivity(_state.XenonConcentration);
        }

        private void EulerStepWithClamp(double dt, double flux, double sigmaF)
        {
            double I = _state.IodineConcentration;
            double Xe = _state.XenonConcentration;

            double dIdt = _gammaI * sigmaF * flux - _lambdaI * I;
            double dXedt = _gammaXe * sigmaF * flux + _lambdaI * I -
                           _lambdaXe * Xe - _sigmaXea * flux * Xe;

            dIdt = ClampDerivative(dIdt, I, dt);
            dXedt = ClampDerivative(dXedt, Xe, dt);

            double newI = I + dIdt * dt;
            double newXe = Xe + dXedt * dt;

            newI = ClampValue(newI);
            newXe = ClampValue(newXe);

            if (double.IsNaN(newI) || double.IsInfinity(newI) || newI < 0)
            {
                newI = MinConcentration;
            }
            if (double.IsNaN(newXe) || double.IsInfinity(newXe) || newXe < 0)
            {
                newXe = MinConcentration;
            }

            if (newI > 0 && I > 0)
            {
                double ratioI = newI / I;
                if (ratioI < MinStepRatio || ratioI > 1.0 / MinStepRatio)
                {
                    newI = I + Math.Sign(newI - I) * Math.Abs(newI - I) * 0.1;
                    newI = ClampValue(newI);
                }
            }

            if (newXe > 0 && Xe > 0)
            {
                double ratioXe = newXe / Xe;
                if (ratioXe < MinStepRatio || ratioXe > 1.0 / MinStepRatio)
                {
                    newXe = Xe + Math.Sign(newXe - Xe) * Math.Abs(newXe - Xe) * 0.1;
                    newXe = ClampValue(newXe);
                }
            }

            _state.IodineConcentration = newI;
            _state.XenonConcentration = newXe;
            _state.IodineDensity = newI;
            _state.XenonDensity = newXe;
        }

        private static double ClampValue(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                return MinConcentration;
            }
            if (value < MinConcentration)
            {
                if (value < 0 && Math.Abs(value) > MinConcentration)
                {
                    return MinConcentration;
                }
                return MinConcentration;
            }
            if (value > MaxConcentration)
            {
                return MaxConcentration;
            }
            return value;
        }

        private static double ClampDerivative(double deriv, double currentValue, double dt)
        {
            if (double.IsNaN(deriv) || double.IsInfinity(deriv))
            {
                return 0.0;
            }

            double maxChange = currentValue * 0.5 + MinConcentration;
            double maxDeriv = maxChange / Math.Max(dt, 1e-9);

            if (Math.Abs(deriv) > maxDeriv)
            {
                return Math.Sign(deriv) * maxDeriv;
            }

            return deriv;
        }

        private double CalculateMaxStableDt(double flux)
        {
            double maxRate = Math.Max(_lambdaI, _lambdaXe);
            maxRate = Math.Max(maxRate, _sigmaXea * flux);
            maxRate = Math.Max(maxRate, 1e-10);
            return 0.1 / maxRate;
        }

        private double CalculateXenonReactivity(double xenonConcentration)
        {
            xenonConcentration = ClampValue(xenonConcentration);

            double sigmaF = _parameters.MacroscopicFissionCrossSection;
            double sigmaA = _sigmaXea;

            if (sigmaF <= 0 || double.IsNaN(sigmaF))
            {
                return 0.0;
            }

            double worth = -(sigmaA * xenonConcentration) / sigmaF;

            const double MaxAbsWorth = 0.05;
            if (double.IsNaN(worth) || double.IsInfinity(worth))
            {
                return 0.0;
            }
            if (worth < -MaxAbsWorth) worth = -MaxAbsWorth;
            if (worth > MaxAbsWorth) worth = MaxAbsWorth;

            return worth;
        }

        public void Reset()
        {
            InitializeEquilibrium();
        }

        public void SetShutdown()
        {
            _state.IsPostShutdown = true;
            _state.TimeSinceShutdown = 0.0;
        }

        public double GetPostShutdownXenonPeakTime()
        {
            double sigmaA = _sigmaXea;
            double flux0 = _fluxNominal;
            double denom = _lambdaXe - _lambdaI;

            if (Math.Abs(denom) < 1e-15)
            {
                return 0.0;
            }

            double ratio = (_lambdaXe + sigmaA * flux0) / (_lambdaI + sigmaA * flux0);
            ratio = Math.Max(1e-10, ratio);

            double tPeak = (1.0 / denom) * Math.Log(ratio);

            if (double.IsNaN(tPeak) || double.IsInfinity(tPeak) || tPeak < 0)
            {
                return 7.0 * 3600.0;
            }

            return Math.Max(0, Math.Min(tPeak, 48.0 * 3600.0));
        }
    }
}
