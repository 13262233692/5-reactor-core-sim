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
        private XenonKineticsSolver? _xenon;

        private double _simulationTime;
        private double _simulationSpeed;
        private double _targetReactivity;
        private double _controlRodReactivity;
        private double _xenonReactivity;
        private double _currentReactivity;
        private double[] _controlRodPositions;
        private double _nominalFlowRate;
        private double _currentFlowRate;
        private double _inletTemperature;
        private double _coolantPressure;
        private bool _isScram;
        private double _timeSinceShutdown;
        private bool _isPostShutdown;

        private const double MaxSimulationSpeed = 200.0;
        private const double MinSimulationSpeed = 0.01;
        private const double NominalPower = 1.0;
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
            _controlRodReactivity = 0.0;
            _xenonReactivity = 0.0;
            _currentReactivity = 0.0;
            _controlRodPositions = new double[4];
            _nominalFlowRate = 18400.0;
            _currentFlowRate = 18400.0;
            _inletTemperature = 292.0;
            _coolantPressure = 15.5e6;
            _isScram = false;
            _timeSinceShutdown = 0.0;
            _isPostShutdown = false;
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

            var xenonParams = XenonKineticsParameters.DefaultPwr();
            _xenon = new XenonKineticsSolver(xenonParams);

            _flowFilter = new CascadedFilter(
                new FirstOrderLowPassFilter(0.5, _nominalFlowRate),
                new MovingAverageFilter(10, _nominalFlowRate)
            );

            _flowNoise = new FlowNoiseGenerator(_nominalFlowRate, seed: 42);

            _simulationTime = 0.0;
            _targetReactivity = 0.0;
            _controlRodReactivity = 0.0;
            _xenonReactivity = 0.0;
            _currentReactivity = 0.0;
            _isScram = false;
            _timeSinceShutdown = 0.0;
            _isPostShutdown = false;

            for (int i = 0; i < 4; i++)
            {
                _controlRodPositions[i] = 0.0;
            }

            _core.UpdatePowerDistribution(kineticsParams.InitialPower * 1e6);
            _xenonReactivity = _xenon.XenonReactivity;
            PublishSnapshot();
        }

        private void RunPhysicsLoop()
        {
            double baseDt = 1.0 / _physicsUpdateRate;
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

                double maxStepTime = 0.25;
                if (accumulator > maxStepTime * _simulationSpeed)
                {
                    accumulator = maxStepTime * _simulationSpeed;
                }

                while (accumulator >= baseDt)
                {
                    ProcessCommands();
                    StepPhysics(baseDt);
                    accumulator -= baseDt;
                }

                PublishSnapshot();

                int sleepMs = (int)(baseDt * 1000 / 4);
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
                        _simulationSpeed = Math.Max(MinSimulationSpeed, Math.Min(MaxSimulationSpeed, cmd.Value));
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

            _controlRodReactivity = -rodWorth;
            _targetReactivity = _controlRodReactivity;
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
            _xenon?.SetShutdown();
        }

        private void ResetSimulation()
        {
            var kineticsParams = PointKineticsParameters.DefaultPwr();
            _kinetics = new PointKineticsSolver(kineticsParams);
            _core = new ReactorCore();

            var xenonParams = XenonKineticsParameters.DefaultPwr();
            _xenon = new XenonKineticsSolver(xenonParams);

            _simulationTime = 0.0;
            _targetReactivity = 0.0;
            _controlRodReactivity = 0.0;
            _xenonReactivity = 0.0;
            _currentReactivity = 0.0;
            _isScram = false;
            _timeSinceShutdown = 0.0;
            _isPostShutdown = false;
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
            if (_kinetics == null || _thermal == null || _core == null || _xenon == null) return;

            double powerFraction = Math.Clamp(_kinetics.Power / NominalPower, 0.0, 10.0);

            _xenon.Step(dt, powerFraction);
            _xenonReactivity = SafeGetXenonReactivity(_xenon);

            double targetTotalReactivity = _targetReactivity + _xenonReactivity;

            double reactivityRate = 0.5;
            double reactivityDelta = targetTotalReactivity - _currentReactivity;
            double maxDelta = 0.01 * dt;
            if (Math.Abs(reactivityDelta) > maxDelta)
            {
                reactivityDelta = Math.Sign(reactivityDelta) * maxDelta;
            }
            _currentReactivity += reactivityRate * reactivityDelta;

            double totalWorthLimit = 0.2;
            if (_currentReactivity < -totalWorthLimit) _currentReactivity = -totalWorthLimit;
            if (_currentReactivity > totalWorthLimit) _currentReactivity = totalWorthLimit;

            if (_isScram)
            {
                _currentReactivity = Math.Min(_currentReactivity, -0.05);
            }

            _kinetics.SetReactivity(_currentReactivity);

            int kineticsSubSteps = GetKineticsSubSteps(dt);
            double subDt = dt / kineticsSubSteps;
            for (int i = 0; i < kineticsSubSteps; i++)
            {
                _kinetics.Step(subDt);
            }

            double totalPower = _kinetics.Power * 1e6;
            totalPower = SafeClampPower(totalPower);

            double rawFlow = _flowNoise != null
                ? _flowNoise.GetFlow(dt)
                : _nominalFlowRate;

            _currentFlowRate = _flowFilter != null
                ? _flowFilter.Filter(rawFlow)
                : _nominalFlowRate;

            _currentFlowRate = SafeClampFlow(_currentFlowRate);

            _thermal.Calculate(
                totalPower,
                _currentFlowRate,
                _inletTemperature,
                _coolantPressure
            );

            _core.UpdatePowerDistribution(totalPower);

            var thermalState = _thermal.State;
            UpdateAssemblyTemperatures(thermalState);

            var xenonState = _xenon.State;
            _isPostShutdown = xenonState.IsPostShutdown;
            _timeSinceShutdown = xenonState.TimeSinceShutdown;

            _simulationTime += dt;
        }

        private static int GetKineticsSubSteps(double dt)
        {
            int baseSteps = 10;
            double maxSubDt = 0.005;
            int calcSteps = (int)Math.Ceiling(dt / maxSubDt);
            return Math.Max(baseSteps, Math.Min(calcSteps, 1000));
        }

        private static double SafeGetXenonReactivity(XenonKineticsSolver xenon)
        {
            try
            {
                double rho = xenon.XenonReactivity;
                if (double.IsNaN(rho) || double.IsInfinity(rho))
                {
                    return 0.0;
                }
                const double maxXenonRho = 0.01;
                return Math.Clamp(rho, -maxXenonRho, maxXenonRho);
            }
            catch
            {
                return 0.0;
            }
        }

        private static double SafeClampPower(double power)
        {
            if (double.IsNaN(power) || double.IsInfinity(power))
            {
                return 1e6;
            }
            if (power < 1e-6) power = 1e-6;
            if (power > 1e10) power = 1e10;
            return power;
        }

        private static double SafeClampFlow(double flow)
        {
            if (double.IsNaN(flow) || double.IsInfinity(flow))
            {
                return 18400.0;
            }
            if (flow < 1000.0) flow = 1000.0;
            if (flow > 50000.0) flow = 50000.0;
            return flow;
        }

        private void UpdateAssemblyTemperatures(ThermalHydraulicsState thermalState)
        {
            if (_core == null) return;

            double avgTemp = thermalState.AverageCoolantTemperature;
            double deltaT = thermalState.OutletTemperature - thermalState.InletTemperature;

            if (double.IsNaN(avgTemp) || double.IsInfinity(avgTemp))
            {
                avgTemp = 300.0;
            }
            if (double.IsNaN(deltaT) || double.IsInfinity(deltaT))
            {
                deltaT = 30.0;
            }

            foreach (var assembly in _core.Assemblies)
            {
                double powerFactor = assembly.PowerFraction * 157.0;

                if (double.IsNaN(powerFactor) || double.IsInfinity(powerFactor))
                {
                    powerFactor = 1.0;
                }
                powerFactor = Math.Clamp(powerFactor, 0.0, 5.0);

                double assemblyDeltaT = deltaT * powerFactor;
                double avgAssemblyTemp = avgTemp + assemblyDeltaT * 0.5;
                double cladTemp = avgAssemblyTemp + 20.0 * powerFactor;
                double dnbr = Math.Max(0.1, thermalState.Dnbr / Math.Max(powerFactor, 0.1));

                if (double.IsNaN(avgAssemblyTemp) || double.IsInfinity(avgAssemblyTemp))
                {
                    avgAssemblyTemp = 310.0;
                }
                if (double.IsNaN(cladTemp) || double.IsInfinity(cladTemp))
                {
                    cladTemp = 330.0;
                }
                if (double.IsNaN(dnbr) || double.IsInfinity(dnbr))
                {
                    dnbr = 2.0;
                }

                assembly.AverageTemperature = Math.Clamp(avgAssemblyTemp, 20.0, 1200.0);
                assembly.CladdingTemperature = Math.Clamp(cladTemp, 20.0, 1500.0);
                assembly.Dnbr = Math.Clamp(dnbr, 0.05, 100.0);
            }
        }

        private void PublishSnapshot()
        {
            if (_kinetics == null || _thermal == null || _core == null) return;

            var thermalState = _thermal.State;
            var kineticsState = _kinetics.State;

            double xenonConc = 0.0;
            double iodineConc = 0.0;
            double xenonWorth = 0.0;
            double timeSinceSd = 0.0;
            bool postSd = false;

            if (_xenon != null)
            {
                try
                {
                    var xs = _xenon.State;
                    xenonConc = double.IsNaN(xs.XenonConcentration) ? 0.0 : xs.XenonConcentration;
                    iodineConc = double.IsNaN(xs.IodineConcentration) ? 0.0 : xs.IodineConcentration;
                    xenonWorth = double.IsNaN(xs.XenonReactivityWorth) ? 0.0 : xs.XenonReactivityWorth;
                    timeSinceSd = double.IsNaN(xs.TimeSinceShutdown) ? 0.0 : xs.TimeSinceShutdown;
                    postSd = xs.IsPostShutdown;
                }
                catch
                {
                    xenonConc = 0.0;
                    iodineConc = 0.0;
                    xenonWorth = 0.0;
                    timeSinceSd = 0.0;
                    postSd = false;
                }
            }

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
                ControlRodPositions = (double[])_controlRodPositions.Clone(),
                Iodine135Concentration = iodineConc,
                Xenon135Concentration = xenonConc,
                XenonReactivityWorth = xenonWorth,
                TimeSinceShutdown = timeSinceSd,
                IsPostShutdown = postSd,
                ControlRodReactivity = _controlRodReactivity,
                SimulationSpeed = _simulationSpeed
            };

            _bus.PublishSnapshot(snapshot);
        }
    }
}
