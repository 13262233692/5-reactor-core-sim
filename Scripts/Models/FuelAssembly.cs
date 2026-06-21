using System;
using System.Collections.Generic;
using System.Numerics;

namespace ReactorCoreSim.Scripts.Models
{
    public struct FuelAssemblyState
    {
        public int Id;
        public int GridX;
        public int GridY;
        public Vector2 Position;
        public double Power;
        public double PowerFraction;
        public double AverageTemperature;
        public double CladdingTemperature;
        public double Dnbr;
        public double Burnup;
        public bool IsControlRod;
        public double ControlRodInsertion;
    }

    public class FuelAssembly
    {
        public int Id { get; }
        public int GridX { get; }
        public int GridY { get; }
        public Vector2 Position { get; }

        public double Power { get; set; }
        public double PowerFraction { get; set; }
        public double AverageTemperature { get; set; }
        public double CladdingTemperature { get; set; }
        public double Dnbr { get; set; }
        public double Burnup { get; set; }
        public bool IsControlRod { get; set; }
        public double ControlRodInsertion { get; set; }
        public double ModeratorTemperatureCoefficient { get; set; }
        public double DopplerCoefficient { get; set; }

        public FuelAssembly(int id, int gridX, int gridY, Vector2 position)
        {
            Id = id;
            GridX = gridX;
            GridY = gridY;
            Position = position;
            Power = 0.0;
            PowerFraction = 1.0 / 157.0;
            AverageTemperature = 300.0;
            CladdingTemperature = 320.0;
            Dnbr = 2.5;
            Burnup = 0.0;
            IsControlRod = false;
            ControlRodInsertion = 0.0;
            ModeratorTemperatureCoefficient = -0.00001;
            DopplerCoefficient = -0.000005;
        }

        public FuelAssemblyState GetState()
        {
            return new FuelAssemblyState
            {
                Id = Id,
                GridX = GridX,
                GridY = GridY,
                Position = Position,
                Power = Power,
                PowerFraction = PowerFraction,
                AverageTemperature = AverageTemperature,
                CladdingTemperature = CladdingTemperature,
                Dnbr = Dnbr,
                Burnup = Burnup,
                IsControlRod = IsControlRod,
                ControlRodInsertion = ControlRodInsertion
            };
        }
    }

    public class ReactorCore
    {
        private readonly List<FuelAssembly> _assemblies;
        public IReadOnlyList<FuelAssembly> Assemblies => _assemblies;

        public int AssemblyCount => _assemblies.Count;
        public double TotalPower { get; private set; }
        public double PeakPowerFactor { get; private set; }
        public double AveragePower { get; private set; }
        public double AverageTemperature { get; private set; }
        public double PeakCladTemperature { get; private set; }
        public double MinimumDnbr { get; private set; }

        private const int GridSize = 17;
        private const double AssemblyPitch = 0.214;

        public ReactorCore()
        {
            _assemblies = new List<FuelAssembly>(157);
            InitializeCoreLayout();
        }

        private void InitializeCoreLayout()
        {
            int center = GridSize / 2;
            int id = 0;
            double maxRadius = 7.5;

            for (int y = 0; y < GridSize; y++)
            {
                for (int x = 0; x < GridSize; x++)
                {
                    double dx = x - center;
                    double dy = y - center;
                    double distance = Math.Sqrt(dx * dx + dy * dy);

                    if (distance <= maxRadius)
                    {
                        var position = new Vector2(
                            (x - center) * (float)AssemblyPitch,
                            (y - center) * (float)AssemblyPitch
                        );

                        var assembly = new FuelAssembly(id, x, y, position);

                        if (IsControlRodPosition(x, y, center))
                        {
                            assembly.IsControlRod = true;
                            assembly.ControlRodInsertion = 0.0;
                        }

                        double radialFactor = 1.0 - 0.3 * (distance / maxRadius);
                        assembly.PowerFraction = radialFactor;

                        _assemblies.Add(assembly);
                        id++;
                    }
                }
            }

            NormalizePowerFractions();
        }

        private static bool IsControlRodPosition(int x, int y, int center)
        {
            int dx = Math.Abs(x - center);
            int dy = Math.Abs(y - center);

            if (dx == 0 && dy == 0) return true;

            int[] ringPattern = { 2, 4, 6 };
            foreach (int r in ringPattern)
            {
                if ((dx == r && dy == 0) || (dx == 0 && dy == r) ||
                    (dx == r && dy == r) || (dx == r && dy == r - 1) ||
                    (dx == r - 1 && dy == r))
                {
                    return true;
                }
            }

            return false;
        }

        private void NormalizePowerFractions()
        {
            double total = 0.0;
            foreach (var assembly in _assemblies)
            {
                total += assembly.PowerFraction;
            }
            foreach (var assembly in _assemblies)
            {
                assembly.PowerFraction /= total;
            }
        }

        public void UpdatePowerDistribution(double totalPower)
        {
            TotalPower = totalPower;
            PeakPowerFactor = 0.0;
            double avgPower = 0.0;
            double avgTemp = 0.0;
            double peakClad = 0.0;
            double minDnbr = double.MaxValue;

            foreach (var assembly in _assemblies)
            {
                double controlRodEffect = 1.0 - assembly.ControlRodInsertion * 0.5;
                double assemblyPower = totalPower * assembly.PowerFraction * controlRodEffect;
                assembly.Power = assemblyPower;

                avgPower += assemblyPower;
                avgTemp += assembly.AverageTemperature;

                if (assemblyPower > PeakPowerFactor * totalPower / AssemblyCount)
                {
                    PeakPowerFactor = assemblyPower * AssemblyCount / totalPower;
                }

                if (assembly.CladdingTemperature > peakClad)
                {
                    peakClad = assembly.CladdingTemperature;
                }

                if (assembly.Dnbr < minDnbr)
                {
                    minDnbr = assembly.Dnbr;
                }
            }

            AveragePower = avgPower / _assemblies.Count;
            AverageTemperature = avgTemp / _assemblies.Count;
            PeakCladTemperature = peakClad;
            MinimumDnbr = minDnbr < double.MaxValue ? minDnbr : 0.0;
        }

        public void SetControlRodGroup(int groupId, double insertion)
        {
            int[] groupPositions = GetControlRodGroup(groupId);

            foreach (var assembly in _assemblies)
            {
                if (!assembly.IsControlRod) continue;

                int groupIndex = GetControlRodGroupIndex(assembly.GridX, assembly.GridY);
                if (groupIndex == groupId)
                {
                    assembly.ControlRodInsertion = Math.Clamp(insertion, 0.0, 1.0);
                }
            }
        }

        private static int GetControlRodGroupIndex(int x, int y)
        {
            int center = GridSize / 2;
            int dx = Math.Abs(x - center);
            int dy = Math.Abs(y - center);
            int dist = Math.Max(dx, dy);

            if (dist == 0) return 0;
            if (dist <= 2) return 1;
            if (dist <= 4) return 2;
            return 3;
        }

        private static int[] GetControlRodGroup(int groupId)
        {
            return groupId switch
            {
                0 => new[] { 1 },
                1 => new[] { 8 },
                2 => new[] { 16 },
                3 => new[] { 24 },
                _ => Array.Empty<int>()
            };
        }

        public FuelAssembly? GetAssemblyAtGrid(int gridX, int gridY)
        {
            foreach (var assembly in _assemblies)
            {
                if (assembly.GridX == gridX && assembly.GridY == gridY)
                {
                    return assembly;
                }
            }
            return null;
        }

        public FuelAssembly? GetNearestAssembly(Vector2 position)
        {
            FuelAssembly? nearest = null;
            double minDist = double.MaxValue;

            foreach (var assembly in _assemblies)
            {
                double dx = position.X - assembly.Position.X;
                double dy = position.Y - assembly.Position.Y;
                double dist = dx * dx + dy * dy;
                if (dist < minDist)
                {
                    minDist = dist;
                    nearest = assembly;
                }
            }

            return nearest;
        }

        public FuelAssemblyState[] GetAllStates()
        {
            var states = new FuelAssemblyState[_assemblies.Count];
            for (int i = 0; i < _assemblies.Count; i++)
            {
                states[i] = _assemblies[i].GetState();
            }
            return states;
        }

        public static Vector2 GridToPosition(int gridX, int gridY)
        {
            int center = GridSize / 2;
            return new Vector2(
                (gridX - center) * (float)AssemblyPitch,
                (gridY - center) * (float)AssemblyPitch
            );
        }

        public static (int x, int y) PositionToGrid(Vector2 position)
        {
            int center = GridSize / 2;
            return (
                (int)Math.Round(position.X / AssemblyPitch) + center,
                (int)Math.Round(position.Y / AssemblyPitch) + center
            );
        }
    }
}
