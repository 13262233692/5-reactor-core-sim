using System;
using System.Threading;
using ReactorCoreSim.Scripts.Core;
using ReactorCoreSim.Scripts.Models;

namespace ReactorCoreSim.Scripts.Systems
{
    public class PhysicsEngine
    {
        private readonly MessageBus _bus;
        private Thread? _thread;
        private volatile bool _running;
        private volatile bool _paused;

        private PointKineticsSolver? _kinetics;
        private ThermalHydraulicsCalculator? _thermal;
        private ReactorCore? _core;
        private ISignalFilter? _flowFilter;
        private FlowNoiseGenerator? _flowNoise;

        private double _simulationTime;
        private double _simulationSpeed;
        private double _targetReactivity;
        private double _currentReactivity;
        private double[] _controlRodPositions;
        private double _nominalFlowRate;
        private double _currentFlowRate;
        private double _inletTemperature;
        private double _coolantPressure;
        private bool _isScram;

        private double _physicsUpdateRate;

        public bool IsRunning => _running;
        public bool IsPaused => _paused;
        public double SimulationTime => _simulationTime;
        public double SimulationSpeed => _simulationSpeed;

        public PhysicsEngine(MessageBus bus)
        {
            _bus = bus;
            _running = false;
            _paused = false;
            _simulationTime = 0.0;
            _simulationSpeed = 1.0;
            _targetReactivity = 0.0;
            _currentReactivity = 0.0;
            _controlRodPositions = new double[4];
            _nominalFlowRate = 18400.0;
            _currentFlowRate = 18400.0;
            _inletTemperature = 292.0;
            _coolantPressure = 15.5e6;
            _isScram = false;
            _physicsUpdateRate = 100.0;
        }

        public void Start()
        {
            if (_running) return;

            Initialize();

            _running = true;
            _paused = false;
            _thread = new Thread(RunPhysicsLoop)
            {
                Name = "PhysicsEngine",
                IsBackground = true,
                Priority = ThreadPriority.AboveNormal
            };
            _thread.Start();
        }

        public void Stop()
        {
            _running = false;
            _thread?.Join(1000);
        }

        public void Pause()
        {
            _paused = true;
        }

        public void Resume()
        {
            _paused = false;
        }

        private void Initialize()
        {
            var kineticsParams = PointKineticsParameters.DefaultPwr();
            _kinetics = new PointKineticsSolver(kineticsParams);

            var thermalParams = ThermalHydraulicsParameters.DefaultPwr();
            _thermal = new ThermalHydraulicsCalculator(thermalParams);

            _core = new ReactorCore();

            _flowFilter = new CascadedFilter(
                new FirstOrderLowPassFilter(0.5, _nominalFlowRate),
                new MovingAverageFilter(10, _nominalFlowRate)
            );

            _flowNoise = new FlowNoiseGenerator(_nominalFlowRate, seed: 42);

            _simulationTime = 0.0;
            _targetReactivity = 0.0;
            _currentReactivity = 0.0;
            _isScram = false;

            for (int i = 0; i < 4; i++)
            {
                _controlRodPositions[i] = 0.0;
            }

            _core.UpdatePowerDistribution(kineticsParams.InitialPower * 1e6);
            PublishSnapshot();
        }

        private void RunPhysicsLoop()
        {
            double dt = 1.0 / _physicsUpdateRate;
            DateTime lastTime = DateTime.UtcNow;
            double accumulator = 0.0;

            while (_running)
            {
                DateTime currentTime = DateTime.UtcNow;
                double realDt = (currentTime - lastTime).TotalSeconds;
                lastTime = currentTime;

                if (_paused)
                {
                    Thread.Sleep(10);
                    continue;
                }

                accumulator += realDt * _simulationSpeed;

                while (accumulator >= dt)
                {
                    ProcessCommands();
                    StepPhysics(dt);
                    accumulator -= dt;
                }

                PublishSnapshot();

                int sleepMs = (int)(dt * 1000 / 4);
                if (sleepMs > 0)
                {
                    Thread.Sleep(Math.Min(sleepMs, 10));
                }
            }
        }

        private void ProcessCommands()
        {
            var commands = _bus.ReceiveAllCommands();

            foreach (var cmd in commands)
            {
                switch (cmd.Type)
                {
                    case ControlCommand.CommandType.SetReactivity:
                        _targetReactivity = cmd.Value;
                        break;

                    case ControlCommand.CommandType.SetControlRod:
                        if (cmd.Index >= 0 && cmd.Index < 4)
                        {
                            _controlRodPositions[cmd.Index] = Math.Clamp(cmd.Value, 0.0, 1.0);
                            _core?.SetControlRodGroup(cmd.Index, cmd.Value);
                            UpdateReactivityFromControlRods();
                        }
                        break;

                    case ControlCommand.CommandType.SetFlowRate:
                        _nominalFlowRate = Math.Max(1000.0, cmd.Value);
                        break;

                    case ControlCommand.CommandType.SetInletTemperature:
                        _inletTemperature = Math.Clamp(cmd.Value, 20.0, 350.0);
                        break;

                    case ControlCommand.CommandType.SetPressure:
                        _coolantPressure = Math.Max(1e5, cmd.Value);
                        break;

                    case ControlCommand.CommandType.Scram:
                        PerformScram();
                        break;

                    case ControlCommand.CommandType.Reset:
                        ResetSimulation();
                        break;

                    case ControlCommand.CommandType.SetSimulationSpeed:
                        _simulationSpeed = Math.Max(0.01, Math.Min(100.0, cmd.Value));
                        break;
                }
            }
        }

        private void UpdateReactivityFromControlRods()
        {
            if (_core == null) return;

            double totalWorth = 0.08;
            double rodWorth = 0.0;

            double[] groupWorths = { 0.15, 0.25, 0.30, 0.30 };

            for (int i = 0; i < 4; i++)
            {
                rodWorth += _controlRodPositions[i] * groupWorths[i] * totalWorth;
            }

            _targetReactivity = -rodWorth;
        }

        private void PerformScram()
        {
            _isScram = true;
            for (int i = 0; i < 4; i++)
            {
                _controlRodPositions[i] = 1.0;
            }
            _core?.SetControlRodGroup(0, 1.0);
            _core?.SetControlRodGroup(1, 1.0);
            _core?.SetControlRodGroup(2, 1.0);
            _core?.SetControlRodGroup(3, 1.0);
            UpdateReactivityFromControlRods();
        }

        private void ResetSimulation()
        {
            var kineticsParams = PointKineticsParameters.DefaultPwr();
            _kinetics = new PointKineticsSolver(kineticsParams);
            _core = new ReactorCore();
            _simulationTime = 0.0;
            _targetReactivity = 0.0;
            _currentReactivity = 0.0;
            _isScram = false;
            _currentFlowRate = _nominalFlowRate;

            for (int i = 0; i < 4; i++)
            {
                _controlRodPositions[i] = 0.0;
            }

            _flowFilter?.Reset(_nominalFlowRate);
            _flowNoise?.Reset();
        }

        private void StepPhysics(double dt)
        {
            if (_kinetics == null || _thermal == null || _core == null) return;

            double reactivityRate = 0.5;
            _currentReactivity += reactivityRate * (_targetReactivity - _currentReactivity) * dt;

            if (_isScram)
            {
                _currentReactivity = Math.Min(_currentReactivity, -0.05);
            }

            _kinetics.SetReactivity(_currentReactivity);

            int subSteps = 10;
            double subDt = dt / subSteps;
            for (int i = 0; i < subSteps; i++)
            {
                _kinetics.Step(subDt);
            }

            double totalPower = _kinetics.Power * 1e6;

            double rawFlow = _flowNoise != null
                ? _flowNoise.GetFlow(dt)
                : _nominalFlowRate;

            _currentFlowRate = _flowFilter != null
                ? _flowFilter.Filter(rawFlow)
                : _nominalFlowRate;

            _thermal.Calculate(
                totalPower,
                _currentFlowRate,
                _inletTemperature,
                _coolantPressure
            );

            _core.UpdatePowerDistribution(totalPower);

            var thermalState = _thermal.State;
            UpdateAssemblyTemperatures(thermalState);

            _simulationTime += dt;
        }

        private void UpdateAssemblyTemperatures(ThermalHydraulicsState thermalState)
        {
            if (_core == null) return;

            double avgTemp = thermalState.AverageCoolantTemperature;
            double deltaT = thermalState.OutletTemperature - thermalState.InletTemperature;

            foreach (var assembly in _core.Assemblies)
            {
                double powerFactor = assembly.PowerFraction * 157.0;
                double assemblyDeltaT = deltaT * powerFactor;
                assembly.AverageTemperature = avgTemp + assemblyDeltaT * 0.5;
                assembly.CladdingTemperature = assembly.AverageTemperature + 20.0 * powerFactor;
                assembly.Dnbr = Math.Max(0.1, thermalState.Dnbr / powerFactor);
            }
        }

        private void PublishSnapshot()
        {
            if (_kinetics == null || _thermal == null || _core == null) return;

            var thermalState = _thermal.State;
            var kineticsState = _kinetics.State;

            var snapshot = new SimulationSnapshot
            {
                Time = _simulationTime,
                TotalPower = _kinetics.Power,
                Reactivity = _currentReactivity,
                NeutronDensity = kineticsState.NeutronDensity,
                PrecursorConcentrations = (double[])kineticsState.PrecursorConcentrations.Clone(),
                AverageTemperature = _core.AverageTemperature,
                PeakCladTemperature = _core.PeakCladTemperature,
                MinimumDnbr = _core.MinimumDnbr,
                MassFlowRate = _currentFlowRate,
                InletTemperature = _inletTemperature,
                OutletTemperature = thermalState.OutletTemperature,
                CoolantPressure = _coolantPressure,
                DoublingTime = _kinetics.GetDoublingTime(),
                AssemblyStates = _core.GetAllStates(),
                IsScram = _isScram,
                ControlRodPositions = (double[])_controlRodPositions.Clone()
            };

            _bus.PublishSnapshot(snapshot);
        }
    }
}
