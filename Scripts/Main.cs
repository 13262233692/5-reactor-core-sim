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

        private SimulationSnapshot _latestSnapshot;
        private double _updateAccumulator;
        private const double UpdateInterval = 1.0 / 30.0;

        private Label? _statusLabel;
        private Label? _speedLabel;
        private bool _paused = false;

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
            paletteBtn.Pressed += () =>
            {
                _coreTileMap?.CycleColorMode();
            };
            hbox.AddChild(paletteBtn);

            var pauseBtn = new Button
            {
                Text = "暂停"
            };
            pauseBtn.Pressed += () =>
            {
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
            scramBtn.Pressed += () =>
            {
                _bus?.SendCommand(new ControlCommand(ControlCommand.CommandType.Scram));
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

        public override void _Process(double delta)
        {
            base._Process(delta);

            if (_bus == null) return;

            _inputSystem?.ProcessContinuousInput(delta);

            _updateAccumulator += delta;
            if (_updateAccumulator >= UpdateInterval)
            {
                _updateAccumulator = 0;
                UpdateDisplay();
            }
        }

        public override void _Input(InputEvent @event)
        {
            base._Input(@event);
            _inputSystem?.ProcessInput(@event, GetProcessDeltaTime());

            if (@event is InputEventKey keyEvent && keyEvent.Pressed)
            {
                HandleKeyPress(keyEvent);
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

            _coreTileMap?.UpdateSnapshot(_latestSnapshot);
            _parameterPanel?.UpdateDisplay(_latestSnapshot);
            _dnbrWarning?.UpdateFromSnapshot(_latestSnapshot);

            UpdateSpeedLabel();
            UpdateStatusLabel();
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
        }
    }
}
