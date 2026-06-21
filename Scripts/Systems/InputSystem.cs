using System;
using Godot;
using ReactorCoreSim.Scripts.Models;
using ReactorCoreSim.Scripts.Systems;

namespace ReactorCoreSim.Scripts.Systems
{
    public class InputSystem
    {
        private readonly MessageBus _bus;
        private readonly float _rodSpeed = 0.5f;
        private readonly float _flowSpeed = 500f;
        private readonly float _tempSpeed = 5f;

        private float[] _targetRodPositions;

        public InputSystem(MessageBus bus)
        {
            _bus = bus;
            _targetRodPositions = new float[4];
        }

        public void ProcessInput(InputEvent @event, double delta)
        {
            if (@event is InputEventKey keyEvent)
            {
                if (keyEvent.Pressed)
                {
                    HandleKeyPress(keyEvent);
                }
            }
        }

        public void ProcessContinuousInput(double delta)
        {
            float dt = (float)delta;

            for (int i = 0; i < 4; i++)
            {
                if (IsRodGroupUp(i))
                {
                    _targetRodPositions[i] = Math.Max(0f, _targetRodPositions[i] - _rodSpeed * dt);
                    SendRodCommand(i, _targetRodPositions[i]);
                }
                if (IsRodGroupDown(i))
                {
                    _targetRodPositions[i] = Math.Min(1f, _targetRodPositions[i] + _rodSpeed * dt);
                    SendRodCommand(i, _targetRodPositions[i]);
                }
            }

            if (Input.IsKeyPressed(Key.Equal) || Input.IsKeyPressed(Key.Plus))
            {
                var snap = _bus.GetLatestSnapshot();
                double newFlow = snap.MassFlowRate + _flowSpeed * dt;
                _bus.SendCommand(new ControlCommand(ControlCommand.CommandType.SetFlowRate, newFlow));
            }
            if (Input.IsKeyPressed(Key.Minus))
            {
                var snap = _bus.GetLatestSnapshot();
                double newFlow = snap.MassFlowRate - _flowSpeed * dt;
                _bus.SendCommand(new ControlCommand(ControlCommand.CommandType.SetFlowRate, Math.Max(5000, newFlow)));
            }
        }

        private static bool IsRodGroupUp(int group)
        {
            return group switch
            {
                0 => Input.IsKeyPressed(Key.Q),
                1 => Input.IsKeyPressed(Key.W),
                2 => Input.IsKeyPressed(Key.E),
                3 => Input.IsKeyPressed(Key.R),
                _ => false
            };
        }

        private static bool IsRodGroupDown(int group)
        {
            return group switch
            {
                0 => Input.IsKeyPressed(Key.A),
                1 => Input.IsKeyPressed(Key.S),
                2 => Input.IsKeyPressed(Key.D),
                3 => Input.IsKeyPressed(Key.F),
                _ => false
            };
        }

        private void HandleKeyPress(InputEventKey keyEvent)
        {
            switch (keyEvent.Keycode)
            {
                case Key.Space:
                    _bus.SendCommand(new ControlCommand(ControlCommand.CommandType.Scram));
                    break;

                case Key.R when keyEvent.CtrlPressed:
                    _bus.SendCommand(new ControlCommand(ControlCommand.CommandType.Reset));
                    break;

                case Key.P:
                    break;

                case Key.Digit1:
                    _bus.SendCommand(new ControlCommand(
                        ControlCommand.CommandType.SetSimulationSpeed, 0.1));
                    break;

                case Key.Digit2:
                    _bus.SendCommand(new ControlCommand(
                        ControlCommand.CommandType.SetSimulationSpeed, 1.0));
                    break;

                case Key.Digit3:
                    _bus.SendCommand(new ControlCommand(
                        ControlCommand.CommandType.SetSimulationSpeed, 10.0));
                    break;

                case Key.Digit4:
                    _bus.SendCommand(new ControlCommand(
                        ControlCommand.CommandType.SetSimulationSpeed, 50.0));
                    break;

                case Key.Digit5:
                    _bus.SendCommand(new ControlCommand(
                        ControlCommand.CommandType.SetSimulationSpeed, 120.0));
                    break;

                case Key.Digit6:
                    _bus.SendCommand(new ControlCommand(
                        ControlCommand.CommandType.SetSimulationSpeed, 200.0));
                    break;

                case Key.I:
                    var snap = _bus.GetLatestSnapshot();
                    double newTemp = snap.InletTemperature + _tempSpeed;
                    _bus.SendCommand(new ControlCommand(
                        ControlCommand.CommandType.SetInletTemperature,
                        Math.Clamp(newTemp, 250.0, 320.0)));
                    break;

                case Key.K:
                    var snap2 = _bus.GetLatestSnapshot();
                    double newTemp2 = snap2.InletTemperature - _tempSpeed;
                    _bus.SendCommand(new ControlCommand(
                        ControlCommand.CommandType.SetInletTemperature,
                        Math.Clamp(newTemp2, 200.0, 300.0)));
                    break;

                case Key.F7:
                    _bus.SendCommand(new ControlCommand(
                        ControlCommand.CommandType.InjectSeismicEvent, 0.10, 8));
                    break;

                case Key.F8:
                    _bus.SendCommand(new ControlCommand(
                        ControlCommand.CommandType.InjectSeismicEvent, 0.20, 10));
                    break;

                case Key.F9:
                    _bus.SendCommand(new ControlCommand(
                        ControlCommand.CommandType.InjectSeismicEvent, 0.35, 12));
                    break;
            }
        }

        private void SendRodCommand(int group, float position)
        {
            _bus.SendCommand(new ControlCommand(
                ControlCommand.CommandType.SetControlRod,
                position,
                group
            ));
        }

        public void InitializeRodPositions(float[] positions)
        {
            for (int i = 0; i < 4 && i < positions.Length; i++)
            {
                _targetRodPositions[i] = positions[i];
            }
        }
    }
}
