using System;

namespace ReactorCoreSim.Scripts.Core
{
    public readonly struct ThermalHydraulicsParameters
    {
        public double NominalPower { get; init; }
        public double MassFlowRate { get; init; }
        public double InletTemperature { get; init; }
        public double CoolantPressure { get; init; }
        public double HeatCapacity { get; init; }
        public double FuelRodDiameter { get; init; }
        public double ActiveFuelHeight { get; init; }
        public double FuelConductivity { get; init; }
        public double CladdingThickness { get; init; }
        public double CladdingConductivity { get; init; }
        public double GapConductance { get; init; }
        public int NumberOfRods { get; init; }

        public static ThermalHydraulicsParameters DefaultPwr()
        {
            return new ThermalHydraulicsParameters
            {
                NominalPower = 3411e6,
                MassFlowRate = 18400.0,
                InletTemperature = 292.0,
                CoolantPressure = 15.5e6,
                HeatCapacity = 5000.0,
                FuelRodDiameter = 0.0095,
                ActiveFuelHeight = 3.66,
                FuelConductivity = 3.0,
                CladdingThickness = 0.00057,
                CladdingConductivity = 16.0,
                GapConductance = 6000.0,
                NumberOfRods = 157
            };
        }
    }

    public struct ThermalHydraulicsState
    {
        public double OutletTemperature;
        public double AverageCoolantTemperature;
        public double CladdingSurfaceTemperature;
        public double FuelCenterlineTemperature;
        public double CriticalHeatFlux;
        public double ActualHeatFlux;
        public double Dnbr;
        public double PressureDrop;
    }

    public class ThermalHydraulicsCalculator
    {
        private readonly ThermalHydraulicsParameters _parameters;
        private ThermalHydraulicsState _state;

        public ThermalHydraulicsState State => _state;

        public ThermalHydraulicsCalculator(ThermalHydraulicsParameters parameters)
        {
            _parameters = parameters;
            _state = new ThermalHydraulicsState();
        }

        public void Calculate(double totalPower, double massFlowRate, double inletTemperature, double pressure)
        {
            double qPerRod = totalPower / _parameters.NumberOfRods;
            double qPerUnitLength = qPerRod / _parameters.ActiveFuelHeight;

            _state.OutletTemperature = inletTemperature +
                totalPower / (massFlowRate * _parameters.HeatCapacity);

            _state.AverageCoolantTemperature = (inletTemperature + _state.OutletTemperature) / 2.0;

            double heatTransferCoeff = CalculateHeatTransferCoefficient(
                _state.AverageCoolantTemperature, pressure, massFlowRate, qPerUnitLength);

            double claddingOuterRadius = _parameters.FuelRodDiameter / 2.0;
            double claddingInnerRadius = claddingOuterRadius - _parameters.CladdingThickness;
            double fuelRadius = claddingInnerRadius;

            _state.ActualHeatFlux = qPerUnitLength / (2.0 * Math.PI * claddingOuterRadius);

            double tCladOuter = _state.AverageCoolantTemperature +
                _state.ActualHeatFlux / heatTransferCoeff;

            double tCladInner = tCladOuter +
                _state.ActualHeatFlux * claddingOuterRadius *
                Math.Log(claddingOuterRadius / claddingInnerRadius) /
                _parameters.CladdingConductivity;

            double gapResistance = 1.0 / _parameters.GapConductance;
            double tFuelSurface = tCladInner +
                _state.ActualHeatFlux * claddingOuterRadius / fuelRadius * gapResistance;

            _state.FuelCenterlineTemperature = tFuelSurface +
                qPerUnitLength / (4.0 * Math.PI * _parameters.FuelConductivity);

            _state.CladdingSurfaceTemperature = tCladOuter;

            _state.CriticalHeatFlux = CalculateCriticalHeatFlux(
                _state.AverageCoolantTemperature, pressure, massFlowRate);

            _state.Dnbr = _state.ActualHeatFlux > 0
                ? _state.CriticalHeatFlux / _state.ActualHeatFlux
                : double.PositiveInfinity;

            _state.PressureDrop = CalculatePressureDrop(massFlowRate, _state.AverageCoolantTemperature);
        }

        private static double CalculateHeatTransferCoefficient(
            double temp, double pressure, double massFlowRate, double qPerUnitLength)
        {
            double density = 720.0 + 0.5 * (15.5e6 - pressure) * 1e-6;
            double viscosity = 90e-6;
            double velocity = massFlowRate / (density * Math.PI * 0.01 * 0.01);
            double hydraulicDiameter = 0.01;
            double reynolds = density * velocity * hydraulicDiameter / viscosity;
            double prandtl = 0.85;
            double nusselt = 0.023 * Math.Pow(reynolds, 0.8) * Math.Pow(prandtl, 0.4);
            double thermalConductivity = 0.5;
            return nusselt * thermalConductivity / hydraulicDiameter;
        }

        private static double CalculateCriticalHeatFlux(double temp, double pressure, double massFlowRate)
        {
            double pRatio = pressure / 22.064e6;
            double baseChf = 1.5e6;

            if (pRatio < 0.5)
            {
                baseChf = 1.8e6;
            }
            else if (pRatio < 0.8)
            {
                baseChf = 1.5e6 + (0.8 - pRatio) * 1e6;
            }
            else
            {
                baseChf = Math.Max(0.2e6, 1.5e6 * (1.0 - (pRatio - 0.8) * 5.0));
            }

            double massFlux = massFlowRate / (Math.PI * 0.01 * 0.01);
            double flowFactor = Math.Pow(massFlux / 3000.0, 0.5);

            return baseChf * Math.Min(2.0, Math.Max(0.5, flowFactor));
        }

        private static double CalculatePressureDrop(double massFlowRate, double avgTemp)
        {
            double density = 740.0 - 0.5 * (avgTemp - 290.0);
            double velocity = massFlowRate / (density * Math.PI * 0.005 * 0.005 * 157);
            double frictionFactor = 0.01;
            double length = 3.66;
            double hydraulicDiameter = 0.01;
            double pressureDrop = frictionFactor * density * velocity * velocity * length /
                (2.0 * hydraulicDiameter);
            return pressureDrop;
        }

        public static double GetSaturationTemperature(double pressure)
        {
            double p = pressure / 1e6;
            if (p <= 0.0) return 100.0;
            double tSat = 100.0;
            if (p < 22.064)
            {
                double a = 8.07131;
                double b = 1730.63;
                double c = 233.426;
                double pKpa = p * 1000.0;
                double logP = Math.Log10(pKpa / 101.325);
                tSat = b / (a - logP) - c;
            }
            else
            {
                tSat = 373.946;
            }
            return tSat + 273.15;
        }
    }
}
