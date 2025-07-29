using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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

    private readonly Dictionary<ulong, IController> _controllers = new();
    private readonly Dictionary<ulong, VigemMapper> _mappers = new();
    private readonly Dictionary<ulong, ControllerInfo> _controllerInfo = new();
    private readonly object _lock = new();

    public event Action<DriverStatus>? StatusUpdated;

    public PokeballVigemDriver()
    {
        _vigemClient = new ViGEmClient();
        _watcher = new BluetoothLEAdvertisementWatcher { ScanningMode = BluetoothLEScanningMode.Active };
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

        Debug.WriteLine($"[*] Found Poké Ball Plus ({args.BluetoothAddress:X}). Attempting to connect...");

        try
        {
            var device = await BluetoothLEDevice.FromBluetoothAddressAsync(args.BluetoothAddress);
            if (device == null)
            {
                Debug.WriteLine($"[-] Failed to get device object for {args.BluetoothAddress:X}.");
                return;
            }

            var controller = new PokeballController(device);
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
                        controller.Disconnected -= OnControllerDisconnected;
                        controller.BatteryLevelUpdated -= OnBatteryLevelUpdated;
                        controller.Dispose();
                        return;
                    }
                    
                    _controllers.Add(controller.BluetoothAddress, controller);
                    _mappers.Add(controller.BluetoothAddress, mapper);
                    _controllerInfo.Add(controller.BluetoothAddress, new ControllerInfo(controller.BluetoothAddress, null));
                }

                Debug.WriteLine($"[+] Successfully connected controller: {args.BluetoothAddress:X}");
            }
            else
            {
                Debug.WriteLine($"[-] Failed to initialize controller: {args.BluetoothAddress:X}");
                controller.Dispose();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[ERROR] Exception while connecting to {args.BluetoothAddress:X}: {ex.Message}");
        }
        finally
        {
            UpdateStatus();
        }
    }

    private void OnBatteryLevelUpdated(IController controller, byte batteryLevel)
    {
        lock (_lock)
        {
            if (_controllerInfo.ContainsKey(controller.BluetoothAddress))
            {
                _controllerInfo[controller.BluetoothAddress] = new ControllerInfo(controller.BluetoothAddress, batteryLevel);
            }
        }
        UpdateStatus();
    }

    private void OnControllerDisconnected(IController controller)
    {
        Debug.WriteLine($"[-] Controller disconnected: {controller.BluetoothAddress:X}");
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