using System;
using Godot;
using ReactorCoreSim.Scripts.Models;
using ReactorCoreSim.Scripts.Systems;

namespace ReactorCoreSim.Scripts.UI
{
    public partial class ParameterPanel : Control
    {
        private Label? _timeLabel;
        private Label? _powerLabel;
        private Label? _reactivityLabel;
        private Label? _flowRateLabel;
        private Label? _tempInLabel;
        private Label? _tempOutLabel;
        private Label? _avgTempLabel;
        private Label? _peakCladLabel;
        private Label? _minDnbrLabel;
        private Label? _pressureLabel;
        private Label? _doublingTimeLabel;
        private Label[]? _rodLabels;
        private Label? _iodineLabel;
        private Label? _xenonLabel;
        private Label? _xenonRhoLabel;
        private Label? _timeSinceShutdownLabel;
        private Label? _speedLabel;
        private Label? _seismicMagLabel;
        private Label? _seismicPeakLabel;
        private Label? _seismicLevelLabel;
        private Label? _hydraulicProgressLabel;
        private Label? _hydraulicStateLabel;
        private Label? _hydraulicPressureLabel;
        private Label? _accidentCauseLabel;

        private VBoxContainer? _mainContainer;
        private PanelContainer? _panel;

        public override void _Ready()
        {
            base._Ready();
            SetupUI();
        }

        private void SetupUI()
        {
            _panel = new PanelContainer
            {
                CustomMinimumSize = new Vector2(280, 0)
            };
            AddChild(_panel);

            _mainContainer = new VBoxContainer
            {
                ThemeTypeVariation = "PanelContainer"
            };
            _panel.AddChild(_mainContainer);

            AddHeader("堆芯参数");

            _timeLabel = CreateParameterRow("仿真时间", "0.0 s");
            _speedLabel = CreateParameterRow("仿真倍速", "1.0x");
            _powerLabel = CreateParameterRow("热功率", "0.0 %");
            _reactivityLabel = CreateParameterRow("总反应性", "0.000 pcm");
            _doublingTimeLabel = CreateParameterRow("倍增时间", "-- s");

            AddSeparator();
            AddHeader("热工水力");

            _flowRateLabel = CreateParameterRow("冷却剂流量", "0.0 kg/s");
            _tempInLabel = CreateParameterRow("入口温度", "0.0 °C");
            _tempOutLabel = CreateParameterRow("出口温度", "0.0 °C");
            _avgTempLabel = CreateParameterRow("平均温度", "0.0 °C");
            _peakCladLabel = CreateParameterRow("峰值包壳温度", "0.0 °C");
            _pressureLabel = CreateParameterRow("冷却剂压力", "0.0 MPa");

            AddSeparator();
            AddHeader("氙毒动力学");

            _iodineLabel = CreateParameterRow("碘-135浓度", "--");
            _xenonLabel = CreateParameterRow("氙-135浓度", "--");
            _xenonRhoLabel = CreateParameterRow("氙毒反应性", "0.000 pcm");
            _timeSinceShutdownLabel = CreateParameterRow("停堆后时间", "0.0 h");

            AddSeparator();
            AddHeader("安全参数");

            _minDnbrLabel = CreateParameterRow("最小 DNBR", "--");

            AddSeparator();
            AddHeader("控制棒组");

            _rodLabels = new Label[4];
            string[] rodNames = { "A 组", "B 组", "C 组", "D 组" };
            for (int i = 0; i < 4; i++)
            {
                _rodLabels[i] = CreateParameterRow(rodNames[i], "0 %");
            }

            AddSeparator();
            AddHeader("地震监测");

            _seismicMagLabel = CreateParameterRow("当前加速度", "0.000g");
            _seismicPeakLabel = CreateParameterRow("峰值加速度", "0.000g");
            _seismicLevelLabel = CreateParameterRow("震级状态", "正常");

            AddSeparator();
            AddHeader("液压停堆系统");

            _hydraulicStateLabel = CreateParameterRow("系统状态", "正常");
            _hydraulicProgressLabel = CreateParameterRow("插入进度", "0.0 %");
            _hydraulicPressureLabel = CreateParameterRow("液压压力", "12.5 MPa");
            _accidentCauseLabel = CreateParameterRow("事故根因", "无");
        }

        private void AddHeader(string text)
        {
            var label = new Label
            {
                Text = text,
                HorizontalAlignment = HorizontalAlignment.Center,
                ThemeTypeVariation = "HeaderLarge"
            };
            label.AddThemeFontSizeOverride("font_size", 16);
            label.AddThemeColorOverride("font_color", new Color(0.4f, 0.7f, 1f));
            _mainContainer?.AddChild(label);
            _mainContainer?.AddChild(new HSeparator());
        }

        private void AddSeparator()
        {
            _mainContainer?.AddChild(new HSeparator { Modulate = new Color(1, 1, 1, 0.2f) });
        }

        private Label CreateParameterRow(string name, string value)
        {
            var container = new HBoxContainer();
            _mainContainer?.AddChild(container);

            var nameLabel = new Label
            {
                Text = name,
                CustomMinimumSize = new Vector2(120, 0),
                ThemeTypeVariation = "Label"
            };
            nameLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.7f));
            container.AddChild(nameLabel);

            var valueLabel = new Label
            {
                Text = value,
                HorizontalAlignment = HorizontalAlignment.Right,
                SizeFlagsHorizontal = SizeFlags.ExpandFill
            };
            valueLabel.AddThemeColorOverride("font_color", new Color(1f, 1f, 1f));
            container.AddChild(valueLabel);

            return valueLabel;
        }

        public void UpdateDisplay(SimulationSnapshot snapshot)
        {
            if (_timeLabel != null)
                _timeLabel.Text = FormatTime(snapshot.Time);

            if (_speedLabel != null)
                _speedLabel.Text = $"{snapshot.SimulationSpeed:F1}x";

            if (_powerLabel != null)
            {
                double powerPercent = snapshot.TotalPower * 100.0;
                _powerLabel.Text = $"{powerPercent:F2} %";
                _powerLabel.Modulate = GetPowerColor(powerPercent);
            }

            if (_reactivityLabel != null)
            {
                double pcm = snapshot.Reactivity * 1e5;
                _reactivityLabel.Text = $"{pcm:F1} pcm";
                _reactivityLabel.Modulate = GetReactivityColor(snapshot.Reactivity);
            }

            if (_iodineLabel != null)
            {
                if (snapshot.Iodine135Concentration > 1e-10)
                {
                    double ratio = snapshot.Iodine135Concentration > 0
                        ? snapshot.Iodine135Concentration / 1e18
                        : 0;
                    _iodineLabel.Text = $"{ratio:F3} × 10¹⁸ m⁻³";
                }
                else
                {
                    _iodineLabel.Text = "< 10⁻¹⁰";
                }
            }

            if (_xenonLabel != null)
            {
                if (snapshot.Xenon135Concentration > 1e-10)
                {
                    double ratio = snapshot.Xenon135Concentration > 0
                        ? snapshot.Xenon135Concentration / 1e18
                        : 0;
                    _xenonLabel.Text = $"{ratio:F3} × 10¹⁸ m⁻³";
                }
                else
                {
                    _xenonLabel.Text = "< 10⁻¹⁰";
                }
            }

            if (_xenonRhoLabel != null)
            {
                double pcm = snapshot.XenonReactivityWorth * 1e5;
                _xenonRhoLabel.Text = $"{pcm:F1} pcm";
                _xenonRhoLabel.Modulate = GetXenonReactivityColor(snapshot.XenonReactivityWorth);
            }

            if (_timeSinceShutdownLabel != null)
            {
                if (snapshot.IsPostShutdown)
                {
                    double hours = snapshot.TimeSinceShutdown / 3600.0;
                    _timeSinceShutdownLabel.Text = $"{hours:F1} h";
                    _timeSinceShutdownLabel.Modulate = new Color(1f, 0.6f, 0.4f);
                }
                else
                {
                    _timeSinceShutdownLabel.Text = "功率运行";
                    _timeSinceShutdownLabel.Modulate = new Color(0.5f, 1f, 0.6f);
                }
            }

            if (_doublingTimeLabel != null)
            {
                if (double.IsPositiveInfinity(snapshot.DoublingTime) || snapshot.DoublingTime > 1e6)
                {
                    _doublingTimeLabel.Text = "-- s";
                }
                else if (snapshot.DoublingTime < 0)
                {
                    _doublingTimeLabel.Text = "-- s";
                }
                else
                {
                    _doublingTimeLabel.Text = $"{snapshot.DoublingTime:F2} s";
                    _doublingTimeLabel.Modulate = GetDoublingTimeColor(snapshot.DoublingTime);
                }
            }

            if (_flowRateLabel != null)
                _flowRateLabel.Text = $"{snapshot.MassFlowRate:F1} kg/s";

            if (_tempInLabel != null)
                _tempInLabel.Text = $"{snapshot.InletTemperature:F1} °C";

            if (_tempOutLabel != null)
                _tempOutLabel.Text = $"{snapshot.OutletTemperature:F1} °C";

            if (_avgTempLabel != null)
                _avgTempLabel.Text = $"{snapshot.AverageTemperature:F1} °C";

            if (_peakCladLabel != null)
            {
                _peakCladLabel.Text = $"{snapshot.PeakCladTemperature:F1} °C";
                _peakCladLabel.Modulate = GetTempColor(snapshot.PeakCladTemperature);
            }

            if (_pressureLabel != null)
                _pressureLabel.Text = $"{snapshot.CoolantPressure / 1e6:F2} MPa";

            if (_minDnbrLabel != null)
            {
                _minDnbrLabel.Text = $"{snapshot.MinimumDnbr:F2}";
                _minDnbrLabel.Modulate = GetDnbrColor(snapshot.MinimumDnbr);
            }

            if (_rodLabels != null && snapshot.ControlRodPositions != null)
            {
                for (int i = 0; i < 4 && i < snapshot.ControlRodPositions.Length; i++)
                {
                    double insertion = snapshot.ControlRodPositions[i] * 100.0;
                    _rodLabels[i].Text = $"{insertion:F0} %";
                    _rodLabels[i].Modulate = GetRodColor(snapshot.ControlRodPositions[i]);
                }
            }

            if (_seismicMagLabel != null)
            {
                double mag = snapshot.SeismicMagnitudeG;
                if (double.IsNaN(mag) || double.IsInfinity(mag)) mag = 0.0;
                _seismicMagLabel.Text = $"{mag:F3}g";
                _seismicMagLabel.Modulate = GetSeismicColor(mag);
            }

            if (_seismicPeakLabel != null)
            {
                double peak = snapshot.SeismicPeakG;
                if (double.IsNaN(peak) || double.IsInfinity(peak)) peak = 0.0;
                _seismicPeakLabel.Text = $"{peak:F3}g";
                _seismicPeakLabel.Modulate = GetSeismicColor(peak);
            }

            if (_seismicLevelLabel != null)
            {
                _seismicLevelLabel.Text = GetSeismicLevelText(snapshot.SeismicEventLevel);
                _seismicLevelLabel.Modulate = GetSeismicLevelColor(snapshot.SeismicEventLevel);
            }

            if (_hydraulicStateLabel != null)
            {
                _hydraulicStateLabel.Text = GetHydraulicStateText(snapshot.HydraulicTripState);
                _hydraulicStateLabel.Modulate = GetHydraulicStateColor(snapshot.HydraulicTripState);
            }

            if (_hydraulicProgressLabel != null)
            {
                double prog = snapshot.HydraulicScramProgress * 100.0;
                if (double.IsNaN(prog) || double.IsInfinity(prog)) prog = 0.0;
                _hydraulicProgressLabel.Text = $"{prog:F1} %";
                _hydraulicProgressLabel.Modulate = snapshot.AllRodsFullyInserted
                    ? new Color(0.4f, 1f, 0.4f)
                    : snapshot.HydraulicTripState > 0
                        ? new Color(1f, 0.7f, 0.3f)
                        : new Color(0.5f, 0.7f, 1f);
            }

            if (_hydraulicPressureLabel != null)
            {
                double p = snapshot.HydraulicPressureMPa;
                if (double.IsNaN(p) || double.IsInfinity(p)) p = 12.5;
                _hydraulicPressureLabel.Text = $"{p:F2} MPa";
            }

            if (_accidentCauseLabel != null)
            {
                _accidentCauseLabel.Text = GetAccidentCauseText(snapshot.AccidentCause);
                _accidentCauseLabel.Modulate = snapshot.AccidentCause != 99
                    ? new Color(1f, 0.4f, 0.4f)
                    : new Color(0.5f, 1f, 0.6f);
            }
        }

        private static Color GetSeismicColor(double g)
        {
            return g switch
            {
                >= 0.15 => new Color(1f, 0.2f, 0.2f),
                >= 0.08 => new Color(1f, 0.6f, 0.2f),
                >= 0.04 => new Color(1f, 1f, 0.3f),
                > 0.005 => new Color(0.7f, 1f, 0.7f),
                _ => new Color(0.5f, 0.7f, 1f)
            };
        }

        private static string GetSeismicLevelText(int level)
        {
            return level switch
            {
                4 => "安全停堆",
                3 => "触发停堆",
                2 => "警报级",
                1 => "通告级",
                _ => "正常"
            };
        }

        private static Color GetSeismicLevelColor(int level)
        {
            return level switch
            {
                >= 3 => new Color(1f, 0.3f, 0.3f),
                2 => new Color(1f, 0.6f, 0.2f),
                1 => new Color(1f, 1f, 0.3f),
                _ => new Color(0.5f, 1f, 0.6f)
            };
        }

        private static string GetHydraulicStateText(int state)
        {
            return state switch
            {
                5 => "紧急安全",
                4 => "棒束到位",
                3 => "下落中",
                2 => "锁闩释放",
                1 => "启动释能",
                _ => "待机"
            };
        }

        private static Color GetHydraulicStateColor(int state)
        {
            return state switch
            {
                >= 4 => new Color(0.4f, 1f, 0.4f),
                >= 2 => new Color(1f, 0.7f, 0.3f),
                1 => new Color(1f, 0.5f, 0.2f),
                _ => new Color(0.5f, 0.7f, 1f)
            };
        }

        private static string GetAccidentCauseText(int cause)
        {
            return cause switch
            {
                0 => "地震触发自动停堆",
                1 => "操作员手动停堆",
                2 => "DNBR越限",
                3 => "压力超限",
                4 => "温度超限",
                _ => "无事故"
            };
        }

        private static string FormatTime(double seconds)
        {
            if (seconds < 60)
                return $"{seconds:F1} s";
            if (seconds < 3600)
                return $"{seconds / 60:F1} min";
            return $"{seconds / 3600:F1} h";
        }

        private static Color GetPowerColor(double powerPercent)
        {
            return powerPercent switch
            {
                > 100 => new Color(1f, 0.3f, 0.3f),
                > 95 => new Color(1f, 0.8f, 0.3f),
                > 50 => new Color(0.5f, 1f, 0.5f),
                _ => new Color(0.5f, 0.7f, 1f)
            };
        }

        private static Color GetReactivityColor(double rho)
        {
            double pcm = rho * 1e5;
            return pcm switch
            {
                > 500 => new Color(1f, 0.3f, 0.3f),
                > 100 => new Color(1f, 0.8f, 0.3f),
                > -100 => new Color(0.5f, 1f, 0.5f),
                > -500 => new Color(0.5f, 0.7f, 1f),
                _ => new Color(0.7f, 0.5f, 1f)
            };
        }

        private static Color GetDoublingTimeColor(double dt)
        {
            return dt switch
            {
                < 10 => new Color(1f, 0.3f, 0.3f),
                < 30 => new Color(1f, 0.8f, 0.3f),
                _ => new Color(0.5f, 1f, 0.5f)
            };
        }

        private static Color GetTempColor(double temp)
        {
            return temp switch
            {
                > 400 => new Color(1f, 0.3f, 0.3f),
                > 350 => new Color(1f, 0.8f, 0.3f),
                _ => new Color(0.5f, 1f, 0.5f)
            };
        }

        private static Color GetDnbrColor(double dnbr)
        {
            return dnbr switch
            {
                < 1.1 => new Color(1f, 0.2f, 0.2f),
                < 1.3 => new Color(1f, 0.8f, 0.3f),
                < 2.0 => new Color(0.8f, 1f, 0.5f),
                _ => new Color(0.5f, 1f, 0.6f)
            };
        }

        private static Color GetRodColor(double insertion)
        {
            return insertion switch
            {
                > 0.9 => new Color(0.7f, 0.3f, 0.3f),
                > 0.5 => new Color(0.8f, 0.8f, 0.3f),
                > 0.1 => new Color(0.5f, 0.8f, 0.5f),
                _ => new Color(0.5f, 0.7f, 1f)
            };
        }

        private static Color GetXenonReactivityColor(double rho)
        {
            double pcm = rho * 1e5;
            return pcm switch
            {
                < -500 => new Color(1f, 0.3f, 0.3f),
                < -200 => new Color(1f, 0.6f, 0.3f),
                < -50 => new Color(1f, 1f, 0.4f),
                _ => new Color(0.7f, 0.7f, 0.7f)
            };
        }
    }
}
