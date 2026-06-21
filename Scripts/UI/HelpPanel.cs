using Godot;

namespace ReactorCoreSim.Scripts.UI
{
    public partial class HelpPanel : Control
    {
        private PanelContainer? _panel;
        private bool _isVisible = true;

        public override void _Ready()
        {
            base._Ready();
            SetupUI();
        }

        private void SetupUI()
        {
            _panel = new PanelContainer
            {
                AnchorLeft = 1,
                AnchorTop = 1,
                AnchorRight = 1,
                AnchorBottom = 1,
                GrowHorizontal = GrowDirection.Begin,
                GrowVertical = GrowDirection.Begin,
                Position = new Vector2(-20, -20)
            };
            AddChild(_panel);

            var vbox = new VBoxContainer();
            _panel.AddChild(vbox);

            var title = new Label
            {
                Text = "操作指南",
                HorizontalAlignment = HorizontalAlignment.Center
            };
            title.AddThemeFontSizeOverride("font_size", 14);
            title.AddThemeColorOverride("font_color", new Color(0.4f, 0.7f, 1f));
            vbox.AddChild(title);

            vbox.AddChild(new HSeparator());

            AddHelpRow(vbox, "Q/W/E/R", "控制棒组 A/B/C/D 提升");
            AddHelpRow(vbox, "A/S/D/F", "控制棒组 A/B/C/D 下插");
            AddHelpRow(vbox, "空格", "紧急停堆 (SCRAM)");
            AddHelpRow(vbox, "+ / -", "增加/减少冷却剂流量");
            AddHelpRow(vbox, "I / K", "提高/降低入口温度");
            AddHelpRow(vbox, "1/2/3/4", "仿真速度 0.1x/1x/10x/50x");
            AddHelpRow(vbox, "5/6", "仿真速度 120x/200x (氙坑模拟)");
            AddHelpRow(vbox, "Ctrl+R", "重置仿真");
            AddHelpRow(vbox, "H", "显示/隐藏帮助");

            var tip = new Label
            {
                Text = "\n提示: 维持 DNBR > 1.3 以确保安全",
                HorizontalAlignment = HorizontalAlignment.Center,
                AutowrapMode = TextServer.AutowrapMode.Word
            };
            tip.AddThemeFontSizeOverride("font_size", 10);
            tip.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.7f));
            vbox.AddChild(tip);

            var hint = new Label
            {
                Text = "按 H 键切换帮助面板",
                HorizontalAlignment = HorizontalAlignment.Center
            };
            hint.AddThemeFontSizeOverride("font_size", 9);
            hint.AddThemeColorOverride("font_color", new Color(0.5f, 0.5f, 0.5f));
            vbox.AddChild(hint);
        }

        private static void AddHelpRow(VBoxContainer parent, string key, string description)
        {
            var row = new HBoxContainer();
            parent.AddChild(row);

            var keyPanel = new PanelContainer
            {
                CustomMinimumSize = new Vector2(100, 0)
            };
            var keyLabel = new Label
            {
                Text = key,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            keyLabel.AddThemeFontSizeOverride("font_size", 10);
            keyLabel.AddThemeColorOverride("font_color", new Color(1f, 1f, 0.8f));
            keyPanel.AddChild(keyLabel);
            row.AddChild(keyPanel);

            var descLabel = new Label
            {
                Text = description
            };
            descLabel.AddThemeFontSizeOverride("font_size", 11);
            descLabel.AddThemeColorOverride("font_color", new Color(0.8f, 0.8f, 0.8f));
            row.AddChild(descLabel);
        }

        public void Toggle()
        {
            _isVisible = !_isVisible;
            if (_panel != null)
            {
                _panel.Visible = _isVisible;
            }
        }

        public override void _Input(InputEvent @event)
        {
            base._Input(@event);
            if (@event is InputEventKey keyEvent && keyEvent.Pressed)
            {
                if (keyEvent.Keycode == Key.H)
                {
                    Toggle();
                }
            }
        }
    }
}
