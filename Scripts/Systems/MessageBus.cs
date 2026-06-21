using System;
using System.Collections.Generic;
using System.Threading;

namespace ReactorCoreSim.Scripts.Models
{
    public readonly struct SimulationSnapshot
    {
        public double Time { get; init; }
        public double TotalPower { get; init; }
        public double Reactivity { get; init; }
        public double NeutronDensity { get; init; }
        public double[] PrecursorConcentrations { get; init; }
        public double AverageTemperature { get; init; }
        public double PeakCladTemperature { get; init; }
        public double MinimumDnbr { get; init; }
        public double MassFlowRate { get; init; }
        public double InletTemperature { get; init; }
        public double OutletTemperature { get; init; }
        public double CoolantPressure { get; init; }
        public double DoublingTime { get; init; }
        public FuelAssemblyState[] AssemblyStates { get; init; }
        public bool IsScram { get; init; }
        public double[] ControlRodPositions { get; init; }
        public double Iodine135Concentration { get; init; }
        public double Xenon135Concentration { get; init; }
        public double XenonReactivityWorth { get; init; }
        public double TimeSinceShutdown { get; init; }
        public bool IsPostShutdown { get; init; }
        public double ControlRodReactivity { get; init; }
        public double SimulationSpeed { get; init; }
        public double SeismicMagnitudeG { get; init; }
        public double SeismicPeakG { get; init; }
        public int SeismicEventLevel { get; init; }
        public bool SeismicTripTriggered { get; init; }
        public double SeismicTripTimestamp { get; init; }
        public double HydraulicScramProgress { get; init; }
        public int HydraulicTripState { get; init; }
        public double HydraulicPressureMPa { get; init; }
        public bool AllRodsFullyInserted { get; init; }
        public double HydraulicTimeSinceTrip { get; init; }
        public int AccidentCause { get; init; }
        public bool UiLocked { get; init; }
        public bool ContainmentWarningActive { get; init; }
    }

    public class ControlCommand
    {
        public enum CommandType
        {
            SetReactivity,
            SetControlRod,
            SetFlowRate,
            SetInletTemperature,
            SetPressure,
            Scram,
            Reset,
            SetSimulationSpeed,
            InjectSeismicEvent,
            InjectSeismicHexData,
            AcknowledgeAccident
        }

        public CommandType Type { get; }
        public double Value { get; }
        public int Index { get; }
        public DateTime Timestamp { get; }

        public ControlCommand(CommandType type, double value = 0.0, int index = 0)
        {
            Type = type;
            Value = value;
            Index = index;
            Timestamp = DateTime.UtcNow;
        }
    }
}

namespace ReactorCoreSim.Scripts.Systems
{
    using ReactorCoreSim.Scripts.Models;

    public class MessageBus
    {
        private readonly Queue<ControlCommand> _commandQueue;
        private readonly object _commandLock = new();
        private SimulationSnapshot _latestSnapshot;
        private readonly object _snapshotLock = new();
        private readonly AutoResetEvent _commandAvailable = new(false);

        public int PendingCommandCount
        {
            get
            {
                lock (_commandLock)
                {
                    return _commandQueue.Count;
                }
            }
        }

        public MessageBus()
        {
            _commandQueue = new Queue<ControlCommand>();
            _latestSnapshot = new SimulationSnapshot
            {
                Time = 0.0,
                TotalPower = 0.0,
                Reactivity = 0.0,
                NeutronDensity = 0.0,
                PrecursorConcentrations = Array.Empty<double>(),
                AverageTemperature = 300.0,
                PeakCladTemperature = 300.0,
                MinimumDnbr = 10.0,
                MassFlowRate = 18000.0,
                InletTemperature = 292.0,
                OutletTemperature = 320.0,
                CoolantPressure = 15.5e6,
                DoublingTime = double.PositiveInfinity,
                AssemblyStates = Array.Empty<FuelAssemblyState>(),
                IsScram = false,
                ControlRodPositions = new double[4],
                Iodine135Concentration = 0.0,
                Xenon135Concentration = 0.0,
                XenonReactivityWorth = 0.0,
                TimeSinceShutdown = 0.0,
                IsPostShutdown = false,
                ControlRodReactivity = 0.0,
                SimulationSpeed = 1.0,
                SeismicMagnitudeG = 0.0,
                SeismicPeakG = 0.0,
                SeismicEventLevel = 0,
                SeismicTripTriggered = false,
                SeismicTripTimestamp = 0.0,
                HydraulicScramProgress = 0.0,
                HydraulicTripState = 0,
                HydraulicPressureMPa = 12.5,
                AllRodsFullyInserted = false,
                HydraulicTimeSinceTrip = 0.0,
                AccidentCause = 99,
                UiLocked = false,
                ContainmentWarningActive = false
            };
        }

        public void SendCommand(ControlCommand command)
        {
            lock (_commandLock)
            {
                _commandQueue.Enqueue(command);
            }
            _commandAvailable.Set();
        }

        public bool TryReceiveCommand(out ControlCommand? command)
        {
            lock (_commandLock)
            {
                if (_commandQueue.Count > 0)
                {
                    command = _commandQueue.Dequeue();
                    return true;
                }
            }
            command = null;
            return false;
        }

        public List<ControlCommand> ReceiveAllCommands()
        {
            var commands = new List<ControlCommand>();
            lock (_commandLock)
            {
                while (_commandQueue.Count > 0)
                {
                    commands.Add(_commandQueue.Dequeue());
                }
            }
            return commands;
        }

        public void WaitForCommand(int timeoutMs = 100)
        {
            _commandAvailable.WaitOne(timeoutMs);
        }

        public void PublishSnapshot(SimulationSnapshot snapshot)
        {
            lock (_snapshotLock)
            {
                _latestSnapshot = snapshot;
            }
        }

        public SimulationSnapshot GetLatestSnapshot()
        {
            lock (_snapshotLock)
            {
                return _latestSnapshot;
            }
        }

        public void ClearCommands()
        {
            lock (_commandLock)
            {
                _commandQueue.Clear();
            }
        }
    }
}
