using System;
using Godot;
using ReactorCoreSim.Scripts.Models;
using ReactorCoreSim.Scripts.Systems;
using ReactorCoreSim.Scripts.Rendering;
using ReactorCoreSim.Scripts.UI;

namespace ReactorCoreSim.Scripts
{
    public partial class Main : Node2D
    {
        private MessageBus? _bus;
        private PhysicsEngine? _physicsEngine;
        private InputSystem? _inputSystem;
        private CoreTileMap? _coreTileMap;
        private ParameterPanel? _parameterPanel;
        private DnbrWarningOverlay? _dnbrWarning;
        private HelpPanel? _helpPanel;

        private ContainmentWarningOverlay? _containmentOverlay;
        private AccidentTraceDialog? _accidentDialog;

        private SimulationSnapshot _latestSnapshot;
        private double _updateAccumulator;
        private const double UpdateInterval = 1.0 / 30.0;

        private Label? _statusLabel;
        private Label? _speedLabel;
        private Button? _pauseBtn;
        private Button? _scramBtn;
        private Button? _tempModeBtn;
        private Button? _dnbrModeBtn;
        private Button? _paletteBtn;
        private bool _paused = false;

        private double _alertCooldownTimer;
        private const double AlertCooldown = 1.5;
        private bool _wasScram;
        private double _prevMinDnbr;
        private AlertAudioManager? _audioManager;

        private bool _containmentActive;
        private bool _uiLocked;
        private bool _accidentDialogShown;
        private bool _wasContainmentActive;

        private double _scramPeakSeismic;
        private double _scramTime;
        private double _scramPowerMW;
        private double _scramDnbr;
        private double _scramPressure;
        private double _scramInletTemp;
        private double _scramRodDepth;
        private double _scramFlow;
        private double _scramXenon;
        private int _scramCauseCode;
        private bool _scramWasAutomatic;

        public override void _Ready()
        {
            base._Ready();
            InitializeSystems();
            SetupUI();
            StartSimulation();
        }

        private void InitializeSystems()
        {
            _bus = new MessageBus();
            _physicsEngine = new PhysicsEngine(_bus);
            _inputSystem = new InputSystem(_bus);
            _latestSnapshot = _bus.GetLatestSnapshot();
            _prevMinDnbr = 10.0;
            _wasScram = false;
            _wasContainmentActive = false;
            _alertCooldownTimer = 0.0;
            _containmentActive = false;
            _uiLocked = false;
            _accidentDialogShown = false;

            _physicsEngine.OnSeismicTrip += HandlePhysicsSeismicTrip;
            _physicsEngine.OnAccidentTripRecorded += HandleAccidentTripRecorded;

            try
            {
                _audioManager = AlertAudioManager.Instance;
                _audioManager.Initialize(this);
            }
            catch
            {
                _audioManager = null;
            }
        }

        private void SetupUI()
        {
            var background = new ColorRect
            {
                Color = new Color(0.02f, 0.02f, 0.05f, 1f),
                AnchorRight = 1,
                AnchorBottom = 1
            };
            AddChild(background);
            MoveChild(background, 0);

            _coreTileMap = new CoreTileMap
            {
                Position = new Vector2(640, 450),
                Scale = new Vector2(1.5f, 1.5f)
            };
            AddChild(_coreTileMap);

            var coreLabel = new Label
            {
                Text = "反应堆堆芯横截面 (157 组件)",
                HorizontalAlignment = HorizontalAlignment.Center,
                Position = new Vector2(640, 60),
                ThemeTypeVariation = "HeaderLarge"
            };
            coreLabel.AddThemeFontSizeOverride("font_size", 20);
            coreLabel.AddThemeColorOverride("font_color", new Color(0.4f, 0.7f, 1f));
            coreLabel.AnchorLeft = 0.5f;
            coreLabel.AnchorRight = 0.5f;
            coreLabel.OffsetLeft = -200;
            coreLabel.OffsetRight = 200;
            AddChild(coreLabel);

            _parameterPanel = new ParameterPanel
            {
                Position = new Vector2(20, 20)
            };
            AddChild(_parameterPanel);

            _dnbrWarning = new DnbrWarningOverlay();
            AddChild(_dnbrWarning);

            _helpPanel = new HelpPanel();
            AddChild(_helpPanel);

            _containmentOverlay = new ContainmentWarningOverlay();
            AddChild(_containmentOverlay);

            SetupBottomBar();
        }

        private void SetupBottomBar()
        {
            var bottomBar = new PanelContainer
            {
                AnchorLeft = 0,
                AnchorTop = 1,
                AnchorRight = 1,
                AnchorBottom = 1,
                GrowVertical = GrowDirection.Begin,
                OffsetTop = -50
            };
            AddChild(bottomBar);

            var hbox = new HBoxContainer
            {
                SizeFlagsVertical = Control.SizeFlags.ShrinkCenter
            };
            bottomBar.AddChild(hbox);

            _statusLabel = new Label
            {
                Text = "状态: 运行中",
                CustomMinimumSize = new Vector2(150, 0)
            };
            _statusLabel.AddThemeColorOverride("font_color", new Color(0.5f, 1f, 0.6f));
            hbox.AddChild(_statusLabel);

            _speedLabel = new Label
            {
                Text = "仿真速度: 1.0x",
                CustomMinimumSize = new Vector2(150, 0)
            };
            hbox.AddChild(_speedLabel);

            var spacer = new Control
            {
                SizeFlagsHorizontal = SizeFlags.ExpandFill
            };
            hbox.AddChild(spacer);

            var powerLabel = new Label
            {
                Text = "功率分布显示",
                CustomMinimumSize = new Vector2(120, 0)
            };
            powerLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.7f));
            hbox.AddChild(powerLabel);

            var tempModeBtn = new Button
            {
                Text = "温度",
                ToggleMode = true,
                ButtonPressed = true
            };
            _tempModeBtn = tempModeBtn;
            tempModeBtn.Pressed += () =>
            {
                if (_coreTileMap != null)
                {
                    _coreTileMap.ShowDnbr = false;
                }
            };
            hbox.AddChild(tempModeBtn);

            var dnbrModeBtn = new Button
            {
                Text = "DNBR",
                ToggleMode = true
            };
            _dnbrModeBtn = dnbrModeBtn;
            dnbrModeBtn.Pressed += () =>
            {
                if (_coreTileMap != null)
                {
                    _coreTileMap.ShowDnbr = true;
                }
            };
            hbox.AddChild(dnbrModeBtn);

            var paletteBtn = new Button
            {
                Text = "配色方案"
            };
            _paletteBtn = paletteBtn;
            paletteBtn.Pressed += () =>
            {
                _coreTileMap?.CycleColorMode();
            };
            hbox.AddChild(paletteBtn);

            var pauseBtn = new Button
            {
                Text = "暂停"
            };
            _pauseBtn = pauseBtn;
            pauseBtn.Pressed += () =>
            {
                if (_uiLocked) return;

                _paused = !_paused;
                if (_physicsEngine != null)
                {
                    if (_paused)
                    {
                        _physicsEngine.Pause();
                        pauseBtn.Text = "继续";
                        if (_statusLabel != null)
                        {
                            _statusLabel.Text = "状态: 已暂停";
                            _statusLabel.Modulate = new Color(1f, 0.8f, 0.3f);
                        }
                    }
                    else
                    {
                        _physicsEngine.Resume();
                        pauseBtn.Text = "暂停";
                        if (_statusLabel != null)
                        {
                            _statusLabel.Text = "状态: 运行中";
                            _statusLabel.Modulate = new Color(0.5f, 1f, 0.6f);
                        }
                    }
                }
            };
            hbox.AddChild(pauseBtn);

            var scramBtn = new Button
            {
                Text = "紧急停堆"
            };
            _scramBtn = scramBtn;
            scramBtn.Pressed += () =>
            {
                if (_uiLocked) return;
                _bus?.SendCommand(new ControlCommand(ControlCommand.CommandType.Scram));
                PlayAlertSafe(AlertSoundType.Scram);
            };
            scramBtn.AddThemeColorOverride("font_color", new Color(1f, 0.3f, 0.3f));
            hbox.AddChild(scramBtn);
        }

        private void StartSimulation()
        {
            _physicsEngine?.Start();

            var initialSnapshot = _bus?.GetLatestSnapshot();
            if (initialSnapshot.HasValue && initialSnapshot.Value.ControlRodPositions != null)
            {
                _inputSystem?.InitializeRodPositions(
                    Array.ConvertAll(initialSnapshot.Value.ControlRodPositions, x => (float)x)
                );
            }
        }

        private void HandlePhysicsSeismicTrip(double peakMag, double tripTime)
        {
            CallDeferred(MethodName.ActivateContainmentWarning, peakMag, tripTime);
        }

        private void HandleAccidentTripRecorded(int causeCode, double powerMW, double dnbr,
            double pressure, double inletTemp, double rodDepth, double flow,
            double xenon, double simTime, bool isAutomatic)
        {
            _scramCauseCode = causeCode;
            _scramPowerMW = powerMW;
            _scramDnbr = dnbr;
            _scramPressure = pressure;
            _scramInletTemp = inletTemp;
            _scramRodDepth = rodDepth;
            _scramFlow = flow;
            _scramXenon = xenon;
            _scramTime = simTime;
            _scramWasAutomatic = isAutomatic;
        }

        public void ActivateContainmentWarning(double peakMag, double tripTime)
        {
            _scramPeakSeismic = peakMag;
            _containmentOverlay?.Activate(peakMag, tripTime, _latestSnapshot.Time);
            _containmentActive = true;
            _uiLocked = true;
            _accidentDialogShown = false;
            ApplyUiLock(true);
            PlayAlertSafe(AlertSoundType.Scram);
        }

        private void ApplyUiLock(bool locked)
        {
            if (_pauseBtn != null) _pauseBtn.Disabled = locked;
            if (_scramBtn != null) _scramBtn.Disabled = locked;
            if (_tempModeBtn != null) _tempModeBtn.Disabled = locked;
            if (_dnbrModeBtn != null) _dnbrModeBtn.Disabled = locked;
            if (_paletteBtn != null) _paletteBtn.Disabled = locked;
        }

        public override void _Process(double delta)
        {
            base._Process(delta);

            if (_bus == null) return;

            _inputSystem?.ProcessContinuousInput(delta);

            if (_alertCooldownTimer > 0)
            {
                _alertCooldownTimer -= delta;
            }

            try
            {
                _audioManager?.CleanupStalePlayers();
            }
            catch
            {
            }

            _updateAccumulator += delta;
            if (_updateAccumulator >= UpdateInterval)
            {
                _updateAccumulator = 0;
                UpdateDisplay();
            }
        }

        public override void _Input(InputEvent @event)
        {
            if (_uiLocked)
            {
                if (@event is InputEventKey keyEvent && keyEvent.Pressed)
                {
                    if (keyEvent.Keycode == Key.H || keyEvent.Keycode == Key.Escape)
                    {
                        GetViewport().SetInputAsHandled();
                        return;
                    }
                }
                GetViewport().SetInputAsHandled();
                return;
            }

            base._Input(@event);
            _inputSystem?.ProcessInput(@event, GetProcessDeltaTime());

            if (@event is InputEventKey keyEvent2 && keyEvent2.Pressed)
            {
                HandleKeyPress(keyEvent2);
            }
        }

        private void HandleKeyPress(InputEventKey keyEvent)
        {
            switch (keyEvent.Keycode)
            {
                case Key.V:
                    _coreTileMap?.CycleColorMode();
                    break;

                case Key.B:
                    if (_coreTileMap != null)
                    {
                        _coreTileMap.ShowDnbr = !_coreTileMap.ShowDnbr;
                    }
                    break;
            }
        }

        private void UpdateDisplay()
        {
            if (_bus == null) return;

            _latestSnapshot = _bus.GetLatestSnapshot();

            CheckAlertTransitions();
            CheckContainmentTransitions();

            _coreTileMap?.UpdateSnapshot(_latestSnapshot);
            _parameterPanel?.UpdateDisplay(_latestSnapshot);
            _dnbrWarning?.UpdateFromSnapshot(_latestSnapshot);

            UpdateSpeedLabel();
            UpdateStatusLabel();

            if (_containmentOverlay != null && _containmentOverlay.IsActive)
            {
                _containmentOverlay.UpdateSimTime(_latestSnapshot.Time);
                _containmentOverlay.UpdateMagnitude(_latestSnapshot.SeismicPeakG);
            }

            _prevMinDnbr = _latestSnapshot.MinimumDnbr;
            _wasScram = _latestSnapshot.IsScram;
            _wasContainmentActive = _latestSnapshot.ContainmentWarningActive;
        }

        private void CheckContainmentTransitions()
        {
            bool snapshotContainment = _latestSnapshot.ContainmentWarningActive;
            bool snapshotUiLocked = _latestSnapshot.UiLocked;

            if (snapshotContainment && !_containmentActive)
            {
                double peakMag = _latestSnapshot.SeismicPeakG;
                if (peakMag <= 0) peakMag = _scramPeakSeismic > 0 ? _scramPeakSeismic : 0.16;

                _scramPeakSeismic = Math.Max(_scramPeakSeismic, peakMag);
                ActivateContainmentWarning(peakMag, _latestSnapshot.SeismicTripTimestamp);
            }

            if (snapshotUiLocked && !_uiLocked)
            {
                _uiLocked = true;
                ApplyUiLock(true);
            }

            if (_containmentActive && !_accidentDialogShown && _latestSnapshot.IsScram)
            {
                ShowAccidentTraceDialog();
            }

            if (_accidentDialog != null && IsInstanceValid(_accidentDialog))
            {
                _accidentDialog.UpdateRodStatus(
                    _latestSnapshot.AllRodsFullyInserted,
                    _latestSnapshot.HydraulicTimeSinceTrip
                );
            }
        }

        private void ShowAccidentTraceDialog()
        {
            if (_accidentDialogShown || _accidentDialog != null) return;

            _accidentDialog = new AccidentTraceDialog();
            _accidentDialog.AccidentAcknowledged += OnAccidentAcknowledged;
            _accidentDialog.LogExportRequested += OnLogExportRequested;

            var record = new AccidentTraceRecord
            {
                EventTimestamp = DateTime.Now,
                SimulationTime = _latestSnapshot.Time,
                RootCause = (AccidentCause)_scramCauseCode,
                PeakSeismicMagnitudeG = Math.Max(_scramPeakSeismic, _latestSnapshot.SeismicPeakG),
                MinDnbrAtTrip = _scramDnbr > 0 ? _scramDnbr : _latestSnapshot.MinimumDnbr,
                PowerAtTripMW = _scramPowerMW > 0 ? _scramPowerMW : _latestSnapshot.TotalPower * 1e3,
                PressureAtTripMPa = _scramPressure > 0 ? _scramPressure : _latestSnapshot.CoolantPressure / 1e6,
                InletTempAtTrip = _scramInletTemp > 0 ? _scramInletTemp : _latestSnapshot.InletTemperature,
                RodInsertionAtTrip = _scramRodDepth,
                FlowRateKgs = _scramFlow > 0 ? _scramFlow : _latestSnapshot.MassFlowRate,
                XenonReactivityAtTrip = _scramXenon,
                WasAutomaticTrip = _scramWasAutomatic,
                Acknowledged = false,
                RodsFullyInserted = _latestSnapshot.AllRodsFullyInserted,
                TimeToFullInsertionSec = _latestSnapshot.HydraulicTimeSinceTrip
            };

            AddChild(_accidentDialog);
            _accidentDialog.PopupCentered();
            _accidentDialog.SetAccidentRecord(record, _latestSnapshot.AllRodsFullyInserted);

            _accidentDialogShown = true;
        }

        private void OnAccidentAcknowledged(AccidentTraceRecord record)
        {
            GD.Print($"[事故确认] {record.EventTimestamp:HH:mm:ss} 根因={record.RootCause} " +
                     $"峰值={record.PeakSeismicMagnitudeG:F3}g 操作员={record.OperatorName}");

            _bus?.SendCommand(new ControlCommand(ControlCommand.CommandType.AcknowledgeAccident));

            if (_containmentOverlay != null)
            {
                _containmentOverlay.Deactivate();
            }
            _containmentActive = false;
        }

        private void OnLogExportRequested(AccidentTraceRecord record)
        {
            GD.Print("[事故日志导出]\n" + record.FormatForLog());
        }

        private void CheckAlertTransitions()
        {
            if (_alertCooldownTimer > 0) return;

            if (!_wasScram && _latestSnapshot.IsScram)
            {
                PlayAlertSafe(AlertSoundType.Scram);
                _alertCooldownTimer = AlertCooldown * 2;
                return;
            }

            if (_prevMinDnbr >= 1.3 && _latestSnapshot.MinimumDnbr < 1.3)
            {
                if (_latestSnapshot.MinimumDnbr < 1.1)
                {
                    PlayAlertSafe(AlertSoundType.Critical);
                }
                else
                {
                    PlayAlertSafe(AlertSoundType.Warning);
                }
                PlayAlertSafe(AlertSoundType.DnbrAlert);
                _alertCooldownTimer = AlertCooldown;
                return;
            }

            if (_prevMinDnbr >= 1.1 && _latestSnapshot.MinimumDnbr < 1.1)
            {
                PlayAlertSafe(AlertSoundType.Critical);
                _alertCooldownTimer = AlertCooldown;
            }
        }

        private void PlayAlertSafe(AlertSoundType type)
        {
            try
            {
                _audioManager?.PlayAlert(type);
            }
            catch
            {
            }
        }

        private void UpdateSpeedLabel()
        {
            if (_speedLabel == null) return;

            double speed = 1.0;
            if (_physicsEngine != null)
            {
                speed = _physicsEngine.SimulationSpeed;
            }
            _speedLabel.Text = $"仿真速度: {speed:F1}x";
        }

        private void UpdateStatusLabel()
        {
            if (_statusLabel == null || _paused) return;

            if (_latestSnapshot.IsScram)
            {
                _statusLabel.Text = "状态: 已停堆";
                _statusLabel.Modulate = new Color(1f, 0.3f, 0.3f);
            }
            else if (_latestSnapshot.MinimumDnbr < 1.1)
            {
                _statusLabel.Text = "状态: 危险!";
                _statusLabel.Modulate = new Color(1f, 0.2f, 0.2f);
            }
            else if (_latestSnapshot.MinimumDnbr < 1.3)
            {
                _statusLabel.Text = "状态: 警告";
                _statusLabel.Modulate = new Color(1f, 0.9f, 0.3f);
            }
            else
            {
                _statusLabel.Text = "状态: 运行正常";
                _statusLabel.Modulate = new Color(0.5f, 1f, 0.6f);
            }
        }

        public override void _ExitTree()
        {
            base._ExitTree();
            _physicsEngine?.Stop();

            try
            {
                _audioManager?.StopAllAlerts();
                _audioManager?.Dispose();
            }
            catch
            {
            }
        }
    }
}
