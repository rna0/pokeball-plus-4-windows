using System;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;

namespace PokeballPlus4Windows.Modularity;

public sealed class VigemMapper : IDisposable
{
    private readonly IController _sourceController;
    private readonly IXbox360Controller _targetController;
    private bool _isDisposed;

    public VigemMapper(IController sourceController, ViGEmClient vigemClient)
    {
        _sourceController = sourceController;
        _targetController = vigemClient.CreateXbox360Controller();

        _sourceController.StateUpdated += OnControllerStateUpdated;
        _sourceController.Disconnected += OnControllerDisconnected;
    }

    public void Connect()
    {
        _targetController.Connect();
    }

    private void OnControllerStateUpdated(IController controller, ControllerState state)
    {
        _targetController.SetButtonState(Xbox360Button.A, state.ButtonA);
        _targetController.SetButtonState(Xbox360Button.B, state.ButtonB);
        _targetController.SetAxisValue(Xbox360Axis.LeftThumbX, (short)(state.AxisX * short.MaxValue));
        _targetController.SetAxisValue(Xbox360Axis.LeftThumbY, (short)(state.AxisY * short.MaxValue));
    }

    private void OnControllerDisconnected(IController controller)
    {
        Dispose();
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        _sourceController.StateUpdated -= OnControllerStateUpdated;
        _sourceController.Disconnected -= OnControllerDisconnected;

        _targetController.Disconnect();
    }
}