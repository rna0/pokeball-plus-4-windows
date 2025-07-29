using System;

namespace PokeballPlus4Windows.Modularity;

public struct ControllerState
{
    public bool ButtonA { get; init; }
    public bool ButtonB { get; init; }
    public float AxisX { get; init; }
    public float AxisY { get; init; }
}

public interface IController : IDisposable
{
    event Action<IController, ControllerState>? StateUpdated;
    event Action<IController>? Disconnected;
    event Action<IController, byte>? BatteryLevelUpdated;
    ulong BluetoothAddress { get; }
}