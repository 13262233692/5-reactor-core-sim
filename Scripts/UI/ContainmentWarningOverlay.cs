using System;
using Godot;

namespace ReactorCoreSim.Scripts.UI
{
    public partial class ContainmentWarningOverlay : CanvasLayer
    {
        private ColorRect _pulseRect;
        private PanelContainer _borderPanel;
        private Label _warningTitle;
        private Label _statusLabel;
        private Label _timerLabel;
        private VBoxContainer _vbox;
        private HBoxContainer _pulseBarHbox;
        private ColorRect[] _pulseBars;

        private double _pulseTimer;
        private double _totalElapsed;
        private bool _isActive;
        private double _currentAlpha;
        private int _pulsePhase;
        private double _seismicMagnitude;
        private double _tripTimestamp;
        private double _simTime;

        private const double PulseFrequency = 4.0;
        private const double MinAlpha = 0.15;
        private const double MaxAlpha = 0.75;
        private const double BarCount = 12;

        [Signal]
        public delegate void WarningDeactivatedEventHandler();

        public bool IsActive => _isActive;

        public ContainmentWarningOverlay()
        {
            Layer = 100;
        }

        public override void _Ready()
        {
            SetupOverlay();
            Visible = false;
            _isActive = false;
            _pulseTimer = 0.0;
            _totalElapsed = 0.0;
            _currentAlpha = 0.0;
            _pulsePhase = 0;
            _seismicMagnitude = 0.0;
            _tripTimestamp = 0.0;
            _simTime = 0.0;
        }

        private void SetupOverlay()
        {
            _pulseRect = new ColorRect
            {
                Color = new Color(0.85f, 0.0f, 0.0f, 0.0f),
                MouseFilter = Control.MouseFilterEnum.Ignore
            };
            AddChild(_pulseRect);

            _borderPanel = new PanelContainer
            {
                MouseFilter = Control.MouseFilterEnum.Ignore
            };

            var style = new StyleBoxFlat
            {
                BgColor = new Color(0, 0, 0, 0),
                BorderColor = new Color(1.0f, 0.1f, 0.1f, 1.0f),
                BorderWidthBottom = 12,
                BorderWidthTop = 12,
                BorderWidthLeft = 12,
                BorderWidthRight = 12,
                CornerRadiusBottomLeft = 0,
                CornerRadiusBottomRight = 0,
                CornerRadiusTopLeft = 0,
                CornerRadiusTopRight = 0
            };
            _borderPanel.AddThemeStyleboxOverride("panel", style);
            AddChild(_borderPanel);

            _vbox = new VBoxContainer
            {
                MouseFilter = Control.MouseFilterEnum.Ignore
            };
            _borderPanel.AddChild(_vbox);

            var topMargin = new Control { CustomMinimumSize = new Vector2(0, 30) };
            _vbox.AddChild(topMargin);

            _warningTitle = new Label
            {
                Text = "⚠ 安全壳封闭警告 ⚠",
                HorizontalAlignment = HorizontalAlignment.Center,
                ThemeTypeVariation = "HeaderLarge"
            };
            _warningTitle.AddThemeFontSizeOverride("font_size", 48);
            _warningTitle.AddThemeColorOverride("font_color", new Color(1.0f, 0.85f, 0.0f));
            _warningTitle.AddThemeFontColorOverride("font_color", new Color(1.0f, 0.85f, 0.0f));
            _vbox.AddChild(_warningTitle);

            var separator1 = new HSeparator();
            _vbox.AddChild(separator1);

            _statusLabel = new Label
            {
                Text = "地震触发自动停堆 — 液压控制棒释放",
                HorizontalAlignment = HorizontalAlignment.Center
            };
            _statusLabel.AddThemeFontSizeOverride("font_size", 24);
            _statusLabel.AddThemeColorOverride("font_color", new Color(1.0f, 0.3f, 0.3f));
            _statusLabel.AddThemeFontColorOverride("font_color", new Color(1.0f, 0.3f, 0.3f));
            _vbox.AddChild(_statusLabel);

            _pulseBarHbox = new HBoxContainer
            {
                Alignment = BoxContainer.AlignmentMode.Center,
                MouseFilter = Control.MouseFilterEnum.Ignore
            };
            _vbox.AddChild(_pulseBarHbox);

            _pulseBars = new ColorRect[(int)BarCount];
            for (int i = 0; i < BarCount; i++)
            {
                var spacer = new Control { CustomMinimumSize = new Vector2(4, 0) };
                _pulseBarHbox.AddChild(spacer);

                _pulseBars[i] = new ColorRect
                {
                    Color = new Color(1.0f, 0.2f, 0.2f, 0.3f),
                    CustomMinimumSize = new Vector2(30, 8),
                    MouseFilter = Control.MouseFilterEnum.Ignore
                };
                _pulseBarHbox.AddChild(_pulseBars[i]);
            }

            _timerLabel = new Label
            {
                Text = "00:00.000",
                HorizontalAlignment = HorizontalAlignment.Center
            };
            _timerLabel.AddThemeFontSizeOverride("font_size", 32);
            _timerLabel.AddThemeColorOverride("font_color", new Color(1.0f, 0.5f, 0.5f));
            _timerLabel.AddThemeFontColorOverride("font_color", new Color(1.0f, 0.5f, 0.5f));
            _vbox.AddChild(_timerLabel);

            var detailLabel = new Label
            {
                Text = "全部操作已锁定 | 请等待事故追溯确认",
                HorizontalAlignment = HorizontalAlignment.Center
            };
            detailLabel.AddThemeFontSizeOverride("font_size", 18);
            detailLabel.AddThemeColorOverride("font_color", new Color(1.0f, 0.6f, 0.6f));
            detailLabel.AddThemeFontColorOverride("font_color", new Color(1.0f, 0.6f, 0.6f));
            _vbox.AddChild(detailLabel);

            var bottomMargin = new Control { CustomMinimumSize = new Vector2(0, 30) };
            _vbox.AddChild(bottomMargin);
        }

        public override void _Process(double delta)
        {
            if (!_isActive) return;

            _pulseTimer += delta;
            _totalElapsed += delta;

            double pulseCycle = 1.0 / PulseFrequency;
            double phase = (_pulseTimer % pulseCycle) / pulseCycle;

            double alphaPulse;
            if (phase < 0.3)
            {
                alphaPulse = MaxAlpha;
            }
            else if (phase < 0.5)
            {
                alphaPulse = MaxAlpha - (phase - 0.3) / 0.2 * (MaxAlpha - MinAlpha);
            }
            else if (phase < 0.8)
            {
                alphaPulse = MinAlpha;
            }
            else
            {
                alphaPulse = MinAlpha + (phase - 0.8) / 0.2 * (MaxAlpha - MinAlpha);
            }

            double targetAlpha = Math.Clamp(alphaPulse, MinAlpha, MaxAlpha);
            _currentAlpha += (targetAlpha - _currentAlpha) * Math.Min(1.0, delta * 20.0);

            UpdateVisuals();

            if (_timerLabel != null)
            {
                double elapsed = _simTime - _tripTimestamp;
                if (elapsed < 0) elapsed = 0;
                int minutes = (int)(elapsed / 60);
                double seconds = elapsed - minutes * 60;
                _timerLabel.Text = $"停堆后 {minutes:00}:{seconds:06.3f} | 峰值 {_seismicMagnitude:F3}g";
            }

            UpdatePulseBars(phase);
        }

        private void UpdateVisuals()
        {
            if (_pulseRect != null)
            {
                float a = (float)_currentAlpha;
                _pulseRect.Color = new Color(0.85f + 0.15f * a, 0.0f, 0.0f, a * 0.7f);
            }

            if (_borderPanel != null)
            {
                var style = _borderPanel.GetThemeStylebox("panel") as StyleBoxFlat;
                if (style != null)
                {
                    style.BorderColor = new Color(1.0f, (float)(0.1 + 0.3 * Math.Sin(_pulseTimer * 20.0)),
                                                   (float)(0.1 + 0.1 * Math.Sin(_pulseTimer * 15.0)), 1.0f);
                    style.BorderWidthBottom = (int)(8 + 6 * Math.Sin(_pulseTimer * PulseFrequency * Math.PI));
                    style.BorderWidthTop = style.BorderWidthBottom;
                    style.BorderWidthLeft = style.BorderWidthBottom;
                    style.BorderWidthRight = style.BorderWidthBottom;
                }
            }

            if (_warningTitle != null)
            {
                float flicker = (float)(0.7 + 0.3 * Math.Sin(_pulseTimer * PulseFrequency * 4.0));
                _warningTitle.Modulate = new Color(1.0f, 0.85f * flicker, 0.0f, 1.0f);
            }
        }

        private void UpdatePulseBars(double phase)
        {
            if (_pulseBars == null) return;

            int activeBar = (int)(phase * BarCount) % (int)BarCount;
            for (int i = 0; i < BarCount; i++)
            {
                if (_pulseBars[i] == null) continue;

                double dist = Math.Abs(i - activeBar);
                if (dist > BarCount / 2) dist = BarCount - dist;

                double brightness = Math.Max(0.15, 1.0 - dist / (BarCount / 2.0));
                _pulseBars[i].Color = new Color(1.0f,
                    (float)(brightness * 0.3),
                    (float)(brightness * 0.2),
                    (float)brightness);
                _pulseBars[i].CustomMinimumSize = new Vector2(30, (float)(4 + brightness * 12));
            }
        }

        public override void _Notification(int what)
        {
            base._Notification(what);

            if (what == NotificationResized)
            {
                if (_pulseRect != null)
                {
                    _pulseRect.Position = Vector2.Zero;
                    _pulseRect.Size = GetViewportRect().Size;
                }

                if (_borderPanel != null)
                {
                    var vpSize = GetViewportRect().Size;
                    _borderPanel.Position = new Vector2(20, 20);
                    _borderPanel.Size = vpSize - new Vector2(40, 40);
                }
            }
        }

        public void Activate(double seismicMagnitude, double tripTimestamp, double currentSimTime)
        {
            if (_isActive) return;

            _isActive = true;
            Visible = true;
            _seismicMagnitude = Math.Max(0.15, seismicMagnitude);
            _tripTimestamp = tripTimestamp;
            _simTime = currentSimTime;
            _pulseTimer = 0.0;
            _totalElapsed = 0.0;
            _currentAlpha = MinAlpha;

            if (_statusLabel != null)
            {
                _statusLabel.Text = $"地震 {_seismicMagnitude:F2}g 触发自动停堆 — 52束控制棒液压释放中";
            }
        }

        public void UpdateSimTime(double simTime)
        {
            _simTime = simTime;
        }

        public void UpdateMagnitude(double magnitude)
        {
            if (magnitude > _seismicMagnitude)
            {
                _seismicMagnitude = magnitude;
            }
        }

        public void Deactivate()
        {
            if (!_isActive) return;

            _isActive = false;
            Visible = false;
            _currentAlpha = 0.0;
            EmitSignal(SignalName.WarningDeactivated);
        }
    }
}
