using System;
using System.Collections.Generic;
using Godot;
using ReactorCoreSim.Scripts.Models;

namespace ReactorCoreSim.Scripts.Rendering
{
    public partial class CoreTileMap : Node2D
    {
        private const int GridSize = 17;
        private const float CellSize = 28f;
        private const float CellGap = 2f;

        private int _colorMode = 0;
        private float _minTemp = 250f;
        private float _maxTemp = 450f;
        private bool _showDnbr = false;
        private bool _showControlRods = true;

        private readonly Dictionary<int, ColorRect> _cellMap = new();
        private readonly Dictionary<int, ColorRect> _rodOverlayMap = new();
        private Node2D? _cellsContainer;
        private Node2D? _rodsContainer;
        private ColorRect? _background;
        private SimulationSnapshot _currentSnapshot;

        [Export]
        public int ColorMode
        {
            get => _colorMode;
            set
            {
                _colorMode = value;
                UpdateVisualization();
            }
        }

        [Export]
        public float MinTemp
        {
            get => _minTemp;
            set
            {
                _minTemp = value;
                UpdateVisualization();
            }
        }

        [Export]
        public float MaxTemp
        {
            get => _maxTemp;
            set
            {
                _maxTemp = value;
                UpdateVisualization();
            }
        }

        [Export]
        public bool ShowDnbr
        {
            get => _showDnbr;
            set
            {
                _showDnbr = value;
                UpdateVisualization();
            }
        }

        [Export]
        public bool ShowControlRods
        {
            get => _showControlRods;
            set
            {
                _showControlRods = value;
                UpdateVisualization();
            }
        }

        public override void _Ready()
        {
            base._Ready();
            SetupBackground();
            SetupCells();
        }

        private void SetupBackground()
        {
            float totalSize = GridSize * (CellSize + CellGap);
            float margin = 20f;

            var bgOuter = new ColorRect
            {
                Color = new Color(0.15f, 0.2f, 0.3f, 0.8f),
                Size = new Vector2(totalSize + margin * 2, totalSize + margin * 2),
                Position = new Vector2(-totalSize / 2 - margin, -totalSize / 2 - margin)
            };
            AddChild(bgOuter);

            _background = new ColorRect
            {
                Color = new Color(0.03f, 0.03f, 0.06f, 1f),
                Size = new Vector2(totalSize, totalSize),
                Position = new Vector2(-totalSize / 2, -totalSize / 2)
            };
            AddChild(_background);

            var border = new ColorRect
            {
                Color = new Color(0.3f, 0.5f, 0.8f, 1f),
                Size = new Vector2(totalSize + 4, totalSize + 4),
                Position = new Vector2(-totalSize / 2 - 2, -totalSize / 2 - 2)
            };
            AddChild(border);
            MoveChild(border, 0);
        }

        private void SetupCells()
        {
            _cellsContainer = new Node2D();
            AddChild(_cellsContainer);

            _rodsContainer = new Node2D();
            AddChild(_rodsContainer);

            float totalSize = GridSize * (CellSize + CellGap);
            float startX = -totalSize / 2 + CellGap / 2;
            float startY = -totalSize / 2 + CellGap / 2;

            int center = GridSize / 2;
            double maxRadius = 7.5;
            int id = 0;

            for (int y = 0; y < GridSize; y++)
            {
                for (int x = 0; x < GridSize; x++)
                {
                    double dx = x - center;
                    double dy = y - center;
                    double distance = Math.Sqrt(dx * dx + dy * dy);

                    if (distance <= maxRadius)
                    {
                        float posX = startX + x * (CellSize + CellGap);
                        float posY = startY + y * (CellSize + CellGap);

                        var cell = new ColorRect
                        {
                            Size = new Vector2(CellSize, CellSize),
                            Position = new Vector2(posX, posY),
                            Color = new Color(0.1f, 0.1f, 0.15f)
                        };
                        _cellsContainer.AddChild(cell);
                        _cellMap[id] = cell;

                        bool isRod = IsControlRodPosition(x, y, center);
                        if (isRod)
                        {
                            var rodOverlay = new ColorRect
                            {
                                Size = new Vector2(CellSize * 0.6f, CellSize * 0.6f),
                                Position = new Vector2(
                                    posX + CellSize * 0.2f,
                                    posY + CellSize * 0.2f
                                ),
                                Color = new Color(0.4f, 0.4f, 0.5f, 0.9f)
                            };
                            _rodsContainer.AddChild(rodOverlay);
                            _rodOverlayMap[id] = rodOverlay;
                        }

                        id++;
                    }
                }
            }
        }

        private static bool IsControlRodPosition(int x, int y, int center)
        {
            int dx = Math.Abs(x - center);
            int dy = Math.Abs(y - center);

            if (dx == 0 && dy == 0) return true;

            int[] ringPattern = { 2, 4, 6 };
            foreach (int r in ringPattern)
            {
                if ((dx == r && dy == 0) || (dx == 0 && dy == r) ||
                    (dx == r && dy == r) || (dx == r && dy == r - 1) ||
                    (dx == r - 1 && dy == r))
                {
                    return true;
                }
            }

            return false;
        }

        public void UpdateSnapshot(SimulationSnapshot snapshot)
        {
            _currentSnapshot = snapshot;
            CallDeferred(MethodName.UpdateVisualization);
        }

        public void UpdateVisualization()
        {
            var snapshot = _currentSnapshot;
            if (snapshot.AssemblyStates == null || snapshot.AssemblyStates.Length == 0)
            {
                return;
            }

            foreach (var assembly in snapshot.AssemblyStates)
            {
                if (_cellMap.TryGetValue(assembly.Id, out var cell))
                {
                    Color tileColor;
                    if (_showDnbr)
                    {
                        tileColor = ThermalColorMapper.MapDnbr((float)assembly.Dnbr, 0.5f, 2.5f);
                    }
                    else
                    {
                        tileColor = ThermalColorMapper.MapTemperature(
                            (float)assembly.CladdingTemperature,
                            _minTemp,
                            _maxTemp,
                            _colorMode
                        );
                    }
                    cell.Color = tileColor;
                }

                if (_showControlRods && _rodOverlayMap.TryGetValue(assembly.Id, out var rod))
                {
                    float alpha = (float)assembly.ControlRodInsertion;
                    rod.Visible = alpha > 0.01f;
                    rod.Color = new Color(0.4f, 0.4f, 0.5f, alpha * 0.9f);
                }
            }
        }

        public override void _Process(double delta)
        {
            base._Process(delta);
        }

        public void SetTemperatureRange(float min, float max)
        {
            _minTemp = min;
            _maxTemp = max;
            UpdateVisualization();
        }

        public void CycleColorMode()
        {
            _colorMode = (_colorMode + 1) % 4;
            UpdateVisualization();
        }
    }
}
