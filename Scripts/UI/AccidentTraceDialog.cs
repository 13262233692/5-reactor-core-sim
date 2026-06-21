using System;
using System.Collections.Generic;
using System.Text;
using Godot;

namespace ReactorCoreSim.Scripts.UI
{
    public enum AccidentCause
    {
        SeismicTrip = 0,
        ManualScram = 1,
        DnbrViolation = 2,
        HighPressure = 3,
        HighTemperature = 4,
        Unknown = 99
    }

    public class AccidentTraceRecord
    {
        public DateTime EventTimestamp { get; set; }
        public double SimulationTime { get; set; }
        public AccidentCause RootCause { get; set; }
        public double PeakSeismicMagnitudeG { get; set; }
        public double MinDnbrAtTrip { get; set; }
        public double PowerAtTripMW { get; set; }
        public double PressureAtTripMPa { get; set; }
        public double InletTempAtTrip { get; set; }
        public double RodInsertionAtTrip { get; set; }
        public double FlowRateKgs { get; set; }
        public bool WasAutomaticTrip { get; set; }
        public string OperatorName { get; set; } = "未指定";
        public string Notes { get; set; } = "";
        public bool Acknowledged { get; set; }
        public bool RodsFullyInserted { get; set; }
        public double TimeToFullInsertionSec { get; set; }
        public double XenonReactivityAtTrip { get; set; }

        public string FormatForLog()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== 停堆事故追溯记录 ===");
            sb.AppendLine($"时间: {EventTimestamp:yyyy-MM-dd HH:mm:ss.fff}");
            sb.AppendLine($"仿真时间: {SimulationTime:F3} s");
            sb.AppendLine($"根因: {RootCause}");
            sb.AppendLine($"峰值地震加速度: {PeakSeismicMagnitudeG:F3}g");
            sb.AppendLine($"停堆时堆功率: {PowerAtTripMW:F2} MW");
            sb.AppendLine($"最小DNBR: {MinDnbrAtTrip:F3}");
            sb.AppendLine($"一回路压力: {PressureAtTripMPa:F2} MPa");
            sb.AppendLine($"入口温度: {InletTempAtTrip:F2} °C");
            sb.AppendLine($"控制棒插入深度: {RodInsertionAtTrip * 100:F1}%");
            sb.AppendLine($"冷却剂流量: {FlowRateKgs:F0} kg/s");
            sb.AppendLine($"自动停堆: {(WasAutomaticTrip ? "是" : "否")}");
            sb.AppendLine($"氙毒反应性: {XenonReactivityAtTrip * 1e5:F1} pcm");
            sb.AppendLine($"控制棒全插入时间: {TimeToFullInsertionSec:F3} s");
            sb.AppendLine($"控制棒到位: {(RodsFullyInserted ? "是" : "否")}");
            sb.AppendLine($"操作员: {OperatorName}");
            sb.AppendLine($"备注: {Notes}");
            sb.AppendLine($"已确认: {(Acknowledged ? "是" : "否")}");
            sb.AppendLine("=========================");
            return sb.ToString();
        }
    }

    public partial class AccidentTraceDialog : AcceptDialog
    {
        private VBoxContainer _mainVbox;
        private Label _titleLabel;
        private Label _causeLabel;
        private Label _timeLabel;
        private Label _powerLabel;
        private Label _seismicLabel;
        private Label _dnbrLabel;
        private Label _rodLabel;
        private Label _statusLabel;
        private Label _xenonLabel;
        private TextEdit _notesEdit;
        private LineEdit _operatorEdit;
        private Button _acknowledgeBtn;
        private Button _exportLogBtn;
        private HBoxContainer _pulseIndicator;
        private ColorRect _indicatorRect;
        private double _pulseTimer;
        private AccidentTraceRecord _record;
        private bool _acknowledgeEnabled;

        [Signal]
        public delegate void AccidentAcknowledgedEventHandler(AccidentTraceRecord record);

        [Signal]
        public delegate void LogExportRequestedEventHandler(AccidentTraceRecord record);

        public AccidentTraceDialog()
        {
            Title = "停堆事故追溯确认";
            Unresizable = true;
            DialogAutoHideOnOk = false;
        }

        public override void _Ready()
        {
            base._Ready();
            _pulseTimer = 0.0;
            _acknowledgeEnabled = false;
            SetupUI();
        }

        private void SetupUI()
        {
            var contentPanel = new PanelContainer();
            var contentStyle = new StyleBoxFlat
            {
                BgColor = new Color(0.12f, 0.02f, 0.02f),
                BorderColor = new Color(0.8f, 0.1f, 0.1f),
                BorderWidthBottom = 3,
                BorderWidthTop = 3,
                BorderWidthLeft = 3,
                BorderWidthRight = 3,
                ContentMarginLeft = 24,
                ContentMarginRight = 24,
                ContentMarginTop = 16,
                ContentMarginBottom = 16
            };
            contentPanel.AddThemeStyleboxOverride("panel", contentStyle);
            AddChild(contentPanel);

            _mainVbox = new VBoxContainer { Size = new Vector2(600, 480) };
            contentPanel.AddChild(_mainVbox);

            _pulseIndicator = new HBoxContainer
            {
                Alignment = BoxContainer.AlignmentMode.Center
            };
            _mainVbox.AddChild(_pulseIndicator);

            _indicatorRect = new ColorRect
            {
                Color = new Color(1.0f, 0.2f, 0.2f, 1.0f),
                CustomMinimumSize = new Vector2(0, 6)
            };
            _pulseIndicator.AddChild(_indicatorRect);

            _titleLabel = new Label
            {
                Text = "⛔ 停堆事故追溯确认 ⛔",
                HorizontalAlignment = HorizontalAlignment.Center
            };
            _titleLabel.AddThemeFontSizeOverride("font_size", 28);
            _titleLabel.AddThemeColorOverride("font_color", new Color(1.0f, 0.85f, 0.2f));
            _mainVbox.AddChild(_titleLabel);

            _mainVbox.AddChild(new HSeparator());

            _causeLabel = CreateInfoLabel("触发原因: --", 22);
            _mainVbox.AddChild(_causeLabel);

            _timeLabel = CreateInfoLabel("停堆时间: --", 18);
            _mainVbox.AddChild(_timeLabel);

            _seismicLabel = CreateInfoLabel("峰值地震加速度: --", 18);
            _mainVbox.AddChild(_seismicLabel);

            _powerLabel = CreateInfoLabel("停堆时堆功率: --", 18);
            _mainVbox.AddChild(_powerLabel);

            _dnbrLabel = CreateInfoLabel("最小 DNBR: --", 18);
            _mainVbox.AddChild(_dnbrLabel);

            _xenonLabel = CreateInfoLabel("氙毒反应性: --", 18);
            _mainVbox.AddChild(_xenonLabel);

            _rodLabel = CreateInfoLabel("控制棒状态: --", 18);
            _mainVbox.AddChild(_rodLabel);

            _statusLabel = CreateInfoLabel("状态: 等待液压到位确认", 18);
            _mainVbox.AddChild(_statusLabel);

            _mainVbox.AddChild(new HSeparator());

            var operatorLabel = new Label { Text = "操作员姓名:" };
            operatorLabel.AddThemeFontSizeOverride("font_size", 16);
            _mainVbox.AddChild(operatorLabel);

            _operatorEdit = new LineEdit
            {
                PlaceholderText = "请输入操作员姓名",
                Editable = false
            };
            _mainVbox.AddChild(_operatorEdit);

            var notesLabel = new Label { Text = "事故备注:" };
            notesLabel.AddThemeFontSizeOverride("font_size", 16);
            _mainVbox.AddChild(notesLabel);

            _notesEdit = new TextEdit
            {
                PlaceholderText = "请输入事故备注信息...",
                CustomMinimumSize = new Vector2(0, 80),
                Editable = false
            };
            _mainVbox.AddChild(_notesEdit);

            var buttonRow = new HBoxContainer
            {
                Alignment = BoxContainer.AlignmentMode.Center
            };
            _mainVbox.AddChild(buttonRow);

            _acknowledgeBtn = new Button
            {
                Text = "确认事故追溯 (锁定控制棒状态)",
                Disabled = true,
                CustomMinimumSize = new Vector2(260, 40)
            };
            _acknowledgeBtn.AddThemeColorOverride("font_color", new Color(1.0f, 0.8f, 0.8f));
            _acknowledgeBtn.Pressed += OnAcknowledgePressed;
            buttonRow.AddChild(_acknowledgeBtn);

            _exportLogBtn = new Button
            {
                Text = "导出事故日志",
                Disabled = true,
                CustomMinimumSize = new Vector2(140, 40)
            };
            _exportLogBtn.Pressed += OnExportLogPressed;
            buttonRow.AddChild(_exportLogBtn);
        }

        private static Label CreateInfoLabel(string text, int fontSize)
        {
            var label = new Label
            {
                Text = text,
                AutowrapMode = TextServer.AutowrapMode.WordSmart
            };
            label.AddThemeFontSizeOverride("font_size", fontSize);
            label.AddThemeColorOverride("font_color", new Color(1.0f, 0.9f, 0.9f));
            return label;
        }

        public override void _Process(double delta)
        {
            base._Process(delta);

            _pulseTimer += delta;
            double pulse = 0.5 + 0.5 * Math.Sin(_pulseTimer * 6.0);

            if (_indicatorRect != null)
            {
                _indicatorRect.Color = new Color(1.0f,
                    (float)(0.1 + 0.3 * pulse),
                    (float)(0.1 + 0.1 * pulse),
                    1.0f);
            }

            if (_titleLabel != null)
            {
                float f = (float)(0.85 + 0.15 * pulse);
                _titleLabel.Modulate = new Color(f, f * 0.85f, 0.2f, 1.0f);
            }
        }

        public void SetAccidentRecord(AccidentTraceRecord record, bool rodsFullyInserted)
        {
            _record = record;
            _acknowledgeEnabled = false;

            if (_causeLabel != null)
            {
                _causeLabel.Text = $"触发原因: {GetCauseDescription(record.RootCause)}";
                _causeLabel.AddThemeColorOverride("font_color", new Color(1.0f, 0.4f, 0.4f));
            }

            if (_timeLabel != null)
            {
                _timeLabel.Text = $"停堆仿真时间: {record.SimulationTime:F3} 秒  " +
                                  $"| 系统时间: {record.EventTimestamp:HH:mm:ss}";
            }

            if (_seismicLabel != null)
            {
                string seismicTxt = record.PeakSeismicMagnitudeG >= 0.15
                    ? $"{record.PeakSeismicMagnitudeG:F3}g (≥ 0.15g 阈值)"
                    : $"{record.PeakSeismicMagnitudeG:F3}g";
                _seismicLabel.Text = $"峰值地震加速度: {seismicTxt}";
            }

            if (_powerLabel != null)
            {
                _powerLabel.Text = $"停堆时堆功率: {record.PowerAtTripMW:F2} MW";
            }

            if (_dnbrLabel != null)
            {
                string dnbrTxt = record.MinDnbrAtTrip < 1.3 ? "警告" : "正常";
                _dnbrLabel.Text = $"最小 DNBR: {record.MinDnbrAtTrip:F3} ({dnbrTxt})";
            }

            if (_xenonLabel != null)
            {
                _xenonLabel.Text = $"氙毒反应性: {record.XenonReactivityAtTrip * 1e5:F1} pcm";
            }

            UpdateRodStatus(rodsFullyInserted, record.TimeToFullInsertionSec);
        }

        public void UpdateRodStatus(bool fullyInserted, double insertionTimeSec)
        {
            if (_record == null) return;
            _record.RodsFullyInserted = fullyInserted;
            _record.TimeToFullInsertionSec = insertionTimeSec;

            if (_rodLabel != null)
            {
                if (fullyInserted)
                {
                    _rodLabel.Text = $"控制棒状态: 全部52束已到位 ({insertionTimeSec:F3} s)";
                    _rodLabel.AddThemeColorOverride("font_color", new Color(0.4f, 1.0f, 0.4f));
                }
                else
                {
                    _rodLabel.Text = "控制棒状态: 液压下落中...";
                    _rodLabel.AddThemeColorOverride("font_color", new Color(1.0f, 0.7f, 0.3f));
                }
            }

            bool canAcknowledge = fullyInserted;
            if (_statusLabel != null)
            {
                _statusLabel.Text = canAcknowledge
                    ? "状态: ✓ 控制棒已就位，可以确认"
                    : "状态: ⏳ 等待控制棒完全插入堆芯底部";
                _statusLabel.AddThemeColorOverride("font_color",
                    canAcknowledge ? new Color(0.4f, 1.0f, 0.4f) : new Color(1.0f, 0.7f, 0.3f));
            }

            SetAcknowledgeEnabled(canAcknowledge);
        }

        private void SetAcknowledgeEnabled(bool enabled)
        {
            _acknowledgeEnabled = enabled;
            if (_acknowledgeBtn != null)
            {
                _acknowledgeBtn.Disabled = !enabled;
                if (enabled)
                {
                    _acknowledgeBtn.AddThemeColorOverride("font_color", new Color(1.0f, 1.0f, 0.7f));
                }
            }
            if (_exportLogBtn != null)
            {
                _exportLogBtn.Disabled = !enabled;
            }
            if (_operatorEdit != null) _operatorEdit.Editable = enabled;
            if (_notesEdit != null) _notesEdit.Editable = enabled;
        }

        private void OnAcknowledgePressed()
        {
            if (_record == null || !_acknowledgeEnabled) return;

            _record.OperatorName = _operatorEdit?.Text?.Trim() ?? "未指定";
            _record.Notes = _notesEdit?.Text ?? "";
            _record.Acknowledged = true;

            GD.Print("[事故追溯] 已确认: " + _record.FormatForLog());

            EmitSignal(SignalName.AccidentAcknowledged, _record);
            QueueFree();
        }

        private void OnExportLogPressed()
        {
            if (_record == null) return;

            _record.OperatorName = _operatorEdit?.Text?.Trim() ?? "未指定";
            _record.Notes = _notesEdit?.Text ?? "";

            EmitSignal(SignalName.LogExportRequested, _record);

            GD.Print("[事故日志] 导出请求:\n" + _record.FormatForLog());
        }

        private static string GetCauseDescription(AccidentCause cause)
        {
            return cause switch
            {
                AccidentCause.SeismicTrip => "地震加速度超限自动停堆 (≥ 0.15g)",
                AccidentCause.ManualScram => "操作员手动触发紧急停堆",
                AccidentCause.DnbrViolation => "DNBR 低于安全限值",
                AccidentCause.HighPressure => "一回路压力超限",
                AccidentCause.HighTemperature => "堆芯出口温度超限",
                _ => "未知触发原因"
            };
        }

        public override void _Input(InputEvent @event)
        {
            if (@event is InputEventKey keyEvent && keyEvent.Pressed)
            {
                if (keyEvent.Keycode == Key.Cancel ||
                    keyEvent.Keycode == Key.Escape)
                {
                    GetViewport().SetInputAsHandled();
                    return;
                }
            }

            if (@event is InputEventMouseButton mouse && mouse.Pressed &&
                mouse.ButtonIndex == MouseButton.Right)
            {
                GetViewport().SetInputAsHandled();
                return;
            }

            base._Input(@event);
        }
    }
}
