using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Nefarius.ViGEm.Client;
using PokeballPlus4Windows.Modularity;

namespace PokeballPlus4Windows;

/// <summary>
/// Discovers controllers and manages their mapping to virtual controllers.
/// </summary>
public sealed class PokeballVigemDriver : IDisposable
{
    private const string PokeballDeviceName = "Pokemon PBP";

    private readonly ViGEmClient _vigemClient;
    private readonly BluetoothLEAdvertisementWatcher _watcher;
    private readonly CemuhookUdpServer _cemuhookUdpServer;

    private readonly Dictionary<ulong, IController> _controllers = new();
    private readonly Dictionary<ulong, VigemMapper> _mappers = new();
    private readonly Dictionary<ulong, ControllerInfo> _controllerInfo = new();
    private readonly object _lock = new();
    private readonly Dictionary<byte, CemuhookPadData> _padStates = new();
    private readonly Dictionary<byte, byte> _padBatteries = new();

    public event Action<DriverStatus>? StatusUpdated;

    public PokeballVigemDriver()
    {
        _vigemClient = new ViGEmClient();
        _watcher = new BluetoothLEAdvertisementWatcher { ScanningMode = BluetoothLEScanningMode.Active };
        _cemuhookUdpServer = new CemuhookUdpServer();
        _cemuhookUdpServer.Start(GetPadDataForSlot);
    }

    public void Start()
    {
        _watcher.Received += OnAdvertisementReceived;
        _watcher.Start();
        UpdateStatus();
    }

    private void OnAdvertisementReceived(BluetoothLEAdvertisementWatcher sender,
        BluetoothLEAdvertisementReceivedEventArgs args)
    {
        // Fire-and-forget to avoid blocking the watcher's thread
        _ = HandleAdvertisementReceivedAsync(args);
    }

    private async Task HandleAdvertisementReceivedAsync(BluetoothLEAdvertisementReceivedEventArgs args)
    {
        lock (_lock)
        {
            if (_controllers.ContainsKey(args.BluetoothAddress))
            {
                return;
            }
        }

        if (string.IsNullOrEmpty(args.Advertisement.LocalName) || args.Advertisement.LocalName != PokeballDeviceName)
        {
            return;
        }

        try
        {
            var device = await BluetoothLEDevice.FromBluetoothAddressAsync(args.BluetoothAddress);
            if (device == null)
            {
                return;
            }

            var controller = new PokeballController(device);
            controller.StateUpdated += OnControllerStateUpdated;
            controller.Disconnected += OnControllerDisconnected;
            controller.BatteryLevelUpdated += OnBatteryLevelUpdated;

            if (await controller.InitializeAsync())
            {
                var mapper = new VigemMapper(controller, _vigemClient);
                mapper.Connect();

                lock (_lock)
                {
                    if (_controllers.ContainsKey(controller.BluetoothAddress))
                    {
                        mapper.Dispose();
                        controller.StateUpdated -= OnControllerStateUpdated;
                        controller.Disconnected -= OnControllerDisconnected;
                        controller.BatteryLevelUpdated -= OnBatteryLevelUpdated;
                        controller.Dispose();
                        return;
                    }

                    _controllers.Add(controller.BluetoothAddress, controller);
                    _mappers.Add(controller.BluetoothAddress, mapper);
                    _controllerInfo.Add(controller.BluetoothAddress, new ControllerInfo(controller.BluetoothAddress, null));
                }
            }
            else
            {
                controller.Dispose();
            }
        }
        catch { }
        finally
        {
            UpdateStatus();
        }
    }

    private CemuhookPadData GetPadDataForSlot(int slot)
    {
        lock (_lock)
        {
            if (_padStates.TryGetValue((byte)slot, out var padData))
            {
                return padData;
            }
            // Return an inactive pad if not present
            return new CemuhookPadData
            {
                padId = (byte)slot,
                padState = 0,
                model = 2,
                connectionType = 1,
                macAddress = new byte[6] { 0x01, 0x02, 0x03, 0x04, 0x05, (byte)slot },
                batteryStatus = 0,
                isActive = 0,
                axisX = 0,
                axisY = 0,
                accelX = 0,
                accelY = 0,
                accelZ = 0,
                gyroX = 0,
                gyroY = 0,
                gyroZ = 0,
                buttons = 0
            };
        }
    }

    private void OnControllerStateUpdated(IController controller, ControllerState state)
    {
        var slot = 0; // Always use padId 0 for the first pad for DSU compatibility
        var battery = _padBatteries.ContainsKey((byte)slot) ? _padBatteries[(byte)slot] : (byte)0x05; // DsBattery.Full
        // Use a realistic MAC address (example: 4C:B9:9B:F9:E8:5C)
        var macAddress = new byte[6] { 0x4C, 0xB9, 0x9B, 0xF9, 0xE8, 0x5C };
        var padData = new CemuhookPadData
        {
            padId = (byte)slot,
            padState = 2, // Connected
            model = 2, // DS4
            connectionType = 2, // Bluetooth
            macAddress = macAddress,
            batteryStatus = battery,
            isActive = 1,
            axisX = state.AxisX,
            axisY = state.AxisY,
            accelX = state.AccelX,
            accelY = state.AccelY,
            accelZ = state.AccelZ,
            gyroX = state.GyroX,
            gyroY = state.GyroY,
            gyroZ = state.GyroZ,
            buttons = (ushort)((state.ButtonA ? 1 : 0) | (state.ButtonB ? 2 : 0))
        };
        lock (_lock)
        {
            _padStates[(byte)slot] = padData;
        }
    }

    private void OnBatteryLevelUpdated(IController controller, byte batteryLevel)
    {
        var slot = (byte)(controller.BluetoothAddress & 0xFF);
        lock (_lock)
        {
            _padBatteries[slot] = batteryLevel;
            if (_controllerInfo.ContainsKey(controller.BluetoothAddress))
            {
                _controllerInfo[controller.BluetoothAddress] = new ControllerInfo(controller.BluetoothAddress, batteryLevel);
            }
        }
        UpdateStatus();
    }

    private void OnControllerDisconnected(IController controller)
    {
        lock (_lock)
        {
            var address = controller.BluetoothAddress;
            if (_mappers.Remove(address, out var mapper))
            {
                mapper.Dispose();
            }

            _controllerInfo.Remove(address);

            if (_controllers.Remove(address, out var storedController))
            {
                storedController.StateUpdated -= OnControllerStateUpdated;
                storedController.Disconnected -= OnControllerDisconnected;
                storedController.BatteryLevelUpdated -= OnBatteryLevelUpdated;
                storedController.Dispose();
            }
        }
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        int count;
        IReadOnlyList<ControllerInfo> infos;
        lock (_lock)
        {
            count = _controllers.Count;
            infos = _controllerInfo.Values.ToList().AsReadOnly();
        }

        string tooltip;
        var isConnected = count > 0;

        switch (count)
        {
            case 0:
                tooltip = "Poké Ball Driver: Scanning...";
                break;
            case 1:
                tooltip = "1 Poké Ball Plus connected.";
                break;
            default:
                tooltip = $"{count} Poké Ball Plus controllers connected.";
                break;
        }

        StatusUpdated?.Invoke(new DriverStatus(tooltip, isConnected, infos));
    }

    public void Dispose()
    {
        _cemuhookUdpServer.Dispose();
        _watcher.Stop();
        _watcher.Received -= OnAdvertisementReceived;

        lock (_lock)
        {
            foreach (var mapper in _mappers.Values)
            {
                mapper.Dispose();
            }
            _mappers.Clear();

            foreach (var controller in _controllers.Values)
            {
                // Unsubscribe from all events to prevent memory leaks
                controller.StateUpdated -= OnControllerStateUpdated;
                controller.Disconnected -= OnControllerDisconnected;
                controller.BatteryLevelUpdated -= OnBatteryLevelUpdated;
                controller.Dispose();
            }
            _controllers.Clear();
            _controllerInfo.Clear();
        }

        _vigemClient.Dispose();
    }
}