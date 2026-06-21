using System;
using Godot;
using ReactorCoreSim.Scripts.Models;
using ReactorCoreSim.Scripts.Rendering;

namespace ReactorCoreSim.Scripts.UI
{
    public partial class DnbrWarningOverlay : Control
    {
        private const double DnbrWarningThreshold = 1.3;
        private const double DnbrCriticalThreshold = 1.1;

        private ColorRect? _warningFlash;
        private Label? _warningLabel;
        private Label? _dnbrValueLabel;
        private PanelContainer? _panel;

        private double _flashTimer;
        private bool _isWarningActive;
        private bool _isCritical;

        [Export]
        public float FlashFrequency { get; set; } = 2.0f;

        public override void _Ready()
        {
            base._Ready();
            SetupUI();
        }

        private void SetupUI()
        {
            _warningFlash = new ColorRect
            {
                Color = new Color(1, 0, 0, 0),
                AnchorRight = 1,
                AnchorBottom = 1,
                MouseFilter = MouseFilterEnum.Ignore
            };
            AddChild(_warningFlash);

            _panel = new PanelContainer
            {
                AnchorRight = 1,
                AnchorBottom = 0,
                GrowHorizontal = GrowDirection.Begin,
                Position = new Vector2(-20, 20),
                MouseFilter = MouseFilterEnum.Ignore
            };
            AddChild(_panel);

            var vbox = new VBoxContainer();
            _panel.AddChild(vbox);

            var titleLabel = new Label
            {
                Text = "DNBR 安全监控",
                HorizontalAlignment = HorizontalAlignment.Center,
                ThemeTypeVariation = "HeaderMedium"
            };
            titleLabel.AddThemeFontSizeOverride("font_size", 14);
            titleLabel.AddThemeColorOverride("font_color", new Color(0.4f, 0.7f, 1f));
            vbox.AddChild(titleLabel);

            vbox.AddChild(new HSeparator());

            _dnbrValueLabel = new Label
            {
                Text = "最小 DNBR: --",
                HorizontalAlignment = HorizontalAlignment.Center
            };
            _dnbrValueLabel.AddThemeFontSizeOverride("font_size", 18);
            _dnbrValueLabel.AddThemeColorOverride("font_color", new Color(0.5f, 1f, 0.6f));
            vbox.AddChild(_dnbrValueLabel);

            _warningLabel = new Label
            {
                Text = "状态: 正常",
                HorizontalAlignment = HorizontalAlignment.Center
            };
            _warningLabel.AddThemeFontSizeOverride("font_size", 14);
            vbox.AddChild(_warningLabel);

            var margin = new Control { CustomMinimumSize = new Vector2(200, 5) };
            vbox.AddChild(margin);

            var barContainer = new HBoxContainer();
            vbox.AddChild(barContainer);

            var colorBar = new ColorBar
            {
                CustomMinimumSize = new Vector2(180, 20),
                MinValue = 0.5f,
                MaxValue = 2.5f,
                Mode = 1
            };
            barContainer.AddChild(colorBar);

            var margin2 = new Control { CustomMinimumSize = new Vector2(180, 5) };
            vbox.AddChild(margin2);

            var scaleLabel = new Label
            {
                Text = "0.5        1.0        1.5        2.0        2.5",
                HorizontalAlignment = HorizontalAlignment.Center
            };
            scaleLabel.AddThemeFontSizeOverride("font_size", 10);
            scaleLabel.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.6f));
            vbox.AddChild(scaleLabel);
        }

        public void UpdateFromSnapshot(SimulationSnapshot snapshot)
        {
            double minDnbr = snapshot.MinimumDnbr;

            if (_dnbrValueLabel != null)
            {
                _dnbrValueLabel.Text = $"最小 DNBR: {minDnbr:F2}";
                _dnbrValueLabel.Modulate = ThermalColorMapper.MapDnbr((float)minDnbr, 0.5f, 2.5f);
            }

            if (minDnbr < DnbrCriticalThreshold)
            {
                _isWarningActive = true;
                _isCritical = true;
                if (_warningLabel != null)
                {
                    _warningLabel.Text = "状态: 危险! CHF 临界";
                    _warningLabel.Modulate = new Color(1f, 0.2f, 0.2f);
                }
            }
            else if (minDnbr < DnbrWarningThreshold)
            {
                _isWarningActive = true;
                _isCritical = false;
                if (_warningLabel != null)
                {
                    _warningLabel.Text = "状态: 警告 接近DNBR限值";
                    _warningLabel.Modulate = new Color(1f, 0.9f, 0.3f);
                }
            }
            else
            {
                _isWarningActive = false;
                _isCritical = false;
                if (_warningLabel != null)
                {
                    _warningLabel.Text = "状态: 正常";
                    _warningLabel.Modulate = new Color(0.5f, 1f, 0.6f);
                }
            }
        }

        public override void _Process(double delta)
        {
            base._Process(delta);

            if (_warningFlash == null) return;

            if (_isWarningActive)
            {
                _flashTimer += delta;
                float t = (float)Math.Sin(_flashTimer * FlashFrequency * Math.PI * 2) * 0.5f + 0.5f;

                if (_isCritical)
                {
                    _warningFlash.Color = new Color(1, 0, 0, 0.3f * t);
                }
                else
                {
                    _warningFlash.Color = new Color(1, 1, 0, 0.15f * t);
                }
            }
            else
            {
                _warningFlash.Color = new Color(1, 0, 0, 0);
                _flashTimer = 0;
            }
        }
    }

    public partial class ColorBar : Control
    {
        private int _colorMode = 0;
        private float _minValue = 0f;
        private float _maxValue = 1f;
        private int _mode = 0;

        [Export]
        public int ColorMode
        {
            get => _colorMode;
            set { _colorMode = value; QueueRedraw(); }
        }

        [Export]
        public float MinValue
        {
            get => _minValue;
            set { _minValue = value; QueueRedraw(); }
        }

        [Export]
        public float MaxValue
        {
            get => _maxValue;
            set { _maxValue = value; QueueRedraw(); }
        }

        [Export]
        public int Mode
        {
            get => _mode;
            set { _mode = value; QueueRedraw(); }
        }

        public override void _Draw()
        {
            base._Draw();

            var rect = GetRect();
            float width = rect.Size.X;
            float height = rect.Size.Y;

            int segments = 100;

            for (int i = 0; i < segments; i++)
            {
                float t1 = (float)i / segments;
                float t2 = (float)(i + 1) / segments;

                Color color1, color2;

                if (_mode == 0)
                {
                    float temp1 = _minValue + t1 * (_maxValue - _minValue);
                    float temp2 = _minValue + t2 * (_maxValue - _minValue);
                    color1 = ThermalColorMapper.MapTemperature(temp1, _minValue, _maxValue, _colorMode);
                    color2 = ThermalColorMapper.MapTemperature(temp2, _minValue, _maxValue, _colorMode);
                }
                else
                {
                    float dnbr1 = _minValue + t1 * (_maxValue - _minValue);
                    float dnbr2 = _minValue + t2 * (_maxValue - _minValue);
                    color1 = ThermalColorMapper.MapDnbr(dnbr1, _minValue, _maxValue);
                    color2 = ThermalColorMapper.MapDnbr(dnbr2, _minValue, _maxValue);
                }

                float x1 = t1 * width;
                float x2 = t2 * width;

                DrawRect(new Rect2(x1, 0, x2 - x1, height), color1);
            }

            DrawRect(new Rect2(0, 0, width, height), new Color(1, 1, 1, 0.3f), false, 1);
        }
    }
}
