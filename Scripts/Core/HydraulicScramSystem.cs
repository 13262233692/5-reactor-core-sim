using System;
using System.Collections.Generic;
using Godot;

namespace ReactorCoreSim.Scripts.Core
{
    public enum RodClusterBank
    {
        BankA = 0,
        BankB = 1,
        BankC = 2,
        BankD = 3
    }

    public enum HydraulicTripState
    {
        Normal = 0,
        TripInitiated = 1,
        LatchesReleased = 2,
        RodsFalling = 3,
        RodsInserted = 4,
        EmergencySecure = 5
    }

    public class HydraulicScramSystem
    {
        public struct RodClusterState
        {
            public int RodCount;
            public double CurrentPosition;
            public double TargetPosition;
            public double FallVelocity;
            public double ActualInsertionDepth;
            public bool IsLatched;
            public bool IsFullyInserted;
            public double TimeSinceTrip;
        }

        private const int TotalRodBundles = 52;
        private const int RodsPerCluster = 13;
        private const double FullStrokeLength = 3.8;
        private const double GravityAccel = 9.81;
        private const double HydraulicDampingCoeff = 2.8;
        private const double MechanicalFriction = 0.15;
        private const double LatchReleaseDelay = 0.025;
        private const double FinalInsertionTolerance = 0.005;
        private const double RodWorthPerCm = 2.5e-5;

        private readonly Dictionary<RodClusterBank, RodClusterState> _clusters;
        private readonly object _stateLock = new();

        private HydraulicTripState _tripState;
        private double _tripTimestamp;
        private double _elapsedSinceTrip;
        private double _totalInsertionFraction;
        private double _hydraulicPressure;
        private double _accumulatorPressure;
        private bool _electromagnetEnergized;
        private bool _scramSolenoidEnergized;

        public HydraulicTripState TripState
        {
            get
            {
                lock (_stateLock) return _tripState;
            }
        }

        public double TotalInsertionFraction
        {
            get
            {
                lock (_stateLock) return _totalInsertionFraction;
            }
        }

        public double TimeSinceTrip
        {
            get
            {
                lock (_stateLock) return _elapsedSinceTrip;
            }
        }

        public bool IsTripped
        {
            get
            {
                lock (_stateLock) return _tripState >= HydraulicTripState.TripInitiated;
            }
        }

        public double HydraulicPressure
        {
            get
            {
                lock (_stateLock) return _hydraulicPressure;
            }
        }

        public bool AllRodsFullyInserted
        {
            get
            {
                lock (_stateLock) return _tripState == HydraulicTripState.RodsInserted ||
                                               _tripState == HydraulicTripState.EmergencySecure;
            }
        }

        public HydraulicScramSystem()
        {
            _clusters = new Dictionary<RodClusterBank, RodClusterState>();
            ResetSystem();
        }

        public void ResetSystem()
        {
            lock (_stateLock)
            {
                _tripState = HydraulicTripState.Normal;
                _tripTimestamp = 0.0;
                _elapsedSinceTrip = 0.0;
                _totalInsertionFraction = 0.0;
                _hydraulicPressure = 12.5;
                _accumulatorPressure = 15.5;
                _electromagnetEnergized = true;
                _scramSolenoidEnergized = false;

                var banks = new[] { RodClusterBank.BankA, RodClusterBank.BankB,
                                    RodClusterBank.BankC, RodClusterBank.BankD };
                foreach (var bank in banks)
                {
                    _clusters[bank] = new RodClusterState
                    {
                        RodCount = RodsPerCluster,
                        CurrentPosition = 0.0,
                        TargetPosition = 0.0,
                        FallVelocity = 0.0,
                        ActualInsertionDepth = 0.0,
                        IsLatched = true,
                        IsFullyInserted = false,
                        TimeSinceTrip = 0.0
                    };
                }
            }
        }

        public void InitiateHydraulicTrip(double simTime)
        {
            lock (_stateLock)
            {
                if (_tripState != HydraulicTripState.Normal) return;

                _tripState = HydraulicTripState.TripInitiated;
                _tripTimestamp = simTime;
                _electromagnetEnergized = false;
                _scramSolenoidEnergized = true;
            }
        }

        public void Update(double dt, double simTime)
        {
            lock (_stateLock)
            {
                if (_tripState == HydraulicTripState.Normal)
                {
                    UpdateNormalHydraulics(dt);
                    return;
                }

                _elapsedSinceTrip = simTime - _tripTimestamp;

                switch (_tripState)
                {
                    case HydraulicTripState.TripInitiated:
                        UpdateTripInitiation(dt);
                        break;
                    case HydraulicTripState.LatchesReleased:
                        UpdateLatchRelease(dt);
                        break;
                    case HydraulicTripState.RodsFalling:
                        UpdateRodsFalling(dt);
                        break;
                    case HydraulicTripState.RodsInserted:
                    case HydraulicTripState.EmergencySecure:
                        UpdateSecureState(dt);
                        break;
                }

                CalculateTotalInsertion();
            }
        }

        private void UpdateNormalHydraulics(double dt)
        {
            double pressureDrift = (_accumulatorPressure - _hydraulicPressure) * 0.05 * dt;
            _hydraulicPressure += pressureDrift;
            _hydraulicPressure = Math.Clamp(_hydraulicPressure, 12.0, 16.0);
        }

        private void UpdateTripInitiation(double dt)
        {
            _hydraulicPressure = Math.Max(0.5, _hydraulicPressure - 40.0 * dt);
            _accumulatorPressure = Math.Max(2.0, _accumulatorPressure - 15.0 * dt);

            if (_elapsedSinceTrip >= LatchReleaseDelay)
            {
                _tripState = HydraulicTripState.LatchesReleased;

                var banks = new[] { RodClusterBank.BankA, RodClusterBank.BankB,
                                    RodClusterBank.BankC, RodClusterBank.BankD };
                foreach (var bank in banks)
                {
                    var state = _clusters[bank];
                    state.IsLatched = false;
                    _clusters[bank] = state;
                }
            }
        }

        private void UpdateLatchRelease(double dt)
        {
            double releaseSpread = 0.015;
            var banks = new[] { RodClusterBank.BankA, RodClusterBank.BankB,
                                RodClusterBank.BankC, RodClusterBank.BankD };

            bool allReleased = true;
            for (int i = 0; i < banks.Length; i++)
            {
                double bankDelay = LatchReleaseDelay + i * releaseSpread;
                if (_elapsedSinceTrip >= bankDelay)
                {
                    var state = _clusters[banks[i]];
                    state.IsLatched = false;
                    state.TimeSinceTrip = _elapsedSinceTrip - bankDelay;
                    _clusters[banks[i]] = state;
                }
                else
                {
                    allReleased = false;
                }
            }

            if (allReleased)
            {
                _tripState = HydraulicTripState.RodsFalling;
            }
        }

        private void UpdateRodsFalling(double dt)
        {
            var banks = new[] { RodClusterBank.BankA, RodClusterBank.BankB,
                                RodClusterBank.BankC, RodClusterBank.BankD };

            bool allInserted = true;

            foreach (var bank in banks)
            {
                var state = _clusters[bank];
                if (state.IsFullyInserted) continue;

                double effectiveGravity = GravityAccel * (1.0 - MechanicalFriction);
                double dampingForce = HydraulicDampingCoeff * state.FallVelocity;
                double buoyancy = 0.3 * GravityAccel;

                double accel = effectiveGravity - buoyancy - dampingForce;
                accel = Math.Max(0.5, accel);

                state.FallVelocity += accel * dt;
                state.FallVelocity = Math.Clamp(state.FallVelocity, 0, 8.0);

                double deltaPos = state.FallVelocity * dt;
                state.ActualInsertionDepth += deltaPos;

                double remaining = FullStrokeLength - state.ActualInsertionDepth;
                if (remaining < 0.2)
                {
                    double snubberFactor = remaining / 0.2;
                    state.FallVelocity *= snubberFactor;
                    state.ActualInsertionDepth = FullStrokeLength -
                        remaining * Math.Exp(-deltaPos / 0.05);
                }

                if (state.ActualInsertionDepth >= FullStrokeLength - FinalInsertionTolerance)
                {
                    state.ActualInsertionDepth = FullStrokeLength;
                    state.FallVelocity = 0.0;
                    state.IsFullyInserted = true;
                }

                state.CurrentPosition = state.ActualInsertionDepth / FullStrokeLength;
                state.TimeSinceTrip += dt;
                _clusters[bank] = state;

                if (!state.IsFullyInserted) allInserted = false;
            }

            if (allInserted)
            {
                _tripState = HydraulicTripState.RodsInserted;
            }
        }

        private void UpdateSecureState(double dt)
        {
            if (_hydraulicPressure > 0.5)
            {
                _hydraulicPressure = Math.Max(0.3, _hydraulicPressure - 2.0 * dt);
            }

            if (_tripState == HydraulicTripState.RodsInserted && _elapsedSinceTrip > 10.0)
            {
                _tripState = HydraulicTripState.EmergencySecure;
            }
        }

        private void CalculateTotalInsertion()
        {
            double totalDepth = 0.0;
            int totalRods = 0;

            var banks = new[] { RodClusterBank.BankA, RodClusterBank.BankB,
                                RodClusterBank.BankC, RodClusterBank.BankD };
            foreach (var bank in banks)
            {
                var state = _clusters[bank];
                totalDepth += state.ActualInsertionDepth * state.RodCount;
                totalRods += state.RodCount;
            }

            double avgDepth = totalDepth / totalRods;
            _totalInsertionFraction = avgDepth / FullStrokeLength;
        }

        public double GetScramReactivityWorth()
        {
            lock (_stateLock)
            {
                double fraction = _totalInsertionFraction;
                if (double.IsNaN(fraction) || double.IsInfinity(fraction))
                {
                    fraction = IsTripped ? 1.0 : 0.0;
                }

                fraction = Math.Clamp(fraction, 0.0, 1.0);

                double totalWorth = -TotalRodBundles * RodWorthPerCm * FullStrokeLength * 100.0;
                double appliedWorth = totalWorth * fraction;

                appliedWorth = Math.Clamp(appliedWorth, -0.30, 0.0);
                return appliedWorth;
            }
        }

        public RodClusterState GetClusterState(RodClusterBank bank)
        {
            lock (_stateLock)
            {
                return _clusters.TryGetValue(bank, out var state) ? state : default;
            }
        }

        public string GetDetailedStatus()
        {
            lock (_stateLock)
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"液压状态: {_tripState}");
                sb.AppendLine($"系统压力: {_hydraulicPressure:F2} MPa");
                sb.AppendLine($"蓄能器: {_accumulatorPressure:F2} MPa");
                sb.AppendLine($"停堆时间: {_elapsedSinceTrip:F3} s");
                sb.AppendLine($"插入进度: {_totalInsertionFraction * 100:F1}%");
                sb.AppendLine($"电磁锁: {(_electromagnetEnergized ? "吸合" : "释放")}");
                sb.AppendLine($"停堆电磁阀: {(_scramSolenoidEnergized ? "励磁" : "断电")}");

                var banks = new[] { RodClusterBank.BankA, RodClusterBank.BankB,
                                    RodClusterBank.BankC, RodClusterBank.BankD };
                foreach (var bank in banks)
                {
                    var s = _clusters[bank];
                    sb.AppendLine($"  {bank}: 位置{s.CurrentPosition * 100:F1}% " +
                                  $"速度{s.FallVelocity:F2}m/s " +
                                  $"{(s.IsFullyInserted ? "到位" : s.IsLatched ? "锁存" : "下落")}");
                }

                return sb.ToString();
            }
        }
    }
}
