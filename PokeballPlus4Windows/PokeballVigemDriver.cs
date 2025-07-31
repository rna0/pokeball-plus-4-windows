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

    private readonly Dictionary<ulong, IController> _controllers = new();
    private readonly Dictionary<ulong, VigemMapper> _mappers = new();
    private readonly Dictionary<ulong, ControllerInfo> _controllerInfo = new();
    private readonly Dictionary<ulong, ControllerState> _latestStates = new();
    private readonly Timer _consolePrintTimer;
    private readonly object _lock = new();

    public event Action<DriverStatus>? StatusUpdated;

    public PokeballVigemDriver()
    {
        _vigemClient = new ViGEmClient();
        _watcher = new BluetoothLEAdvertisementWatcher { ScanningMode = BluetoothLEScanningMode.Active };
        // Set up a timer to call PrintGyroToConsole every 0.2 seconds.
        _consolePrintTimer = new Timer(PrintGyroToConsole, null, TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(200));
    }

    public void Start()
    {
        _watcher.Received += OnAdvertisementReceived;
        _watcher.Start();
        UpdateStatus();
    }

    private void PrintGyroToConsole(object? state)
    {
        lock (_lock)
        {
            if (_latestStates.Count == 0) return;

            Debug.WriteLine($"--- Gyroscope Data ({DateTime.Now:HH:mm:ss}) ---");

            foreach (var (address, controllerState) in _latestStates)
            {
                var addressHex = address.ToString("X");
                var shortAddress = addressHex.Length > 4 ? addressHex.Substring(addressHex.Length - 4) : addressHex;

                // Output the raw data first for reference
                Debug.WriteLine($"Controller ({shortAddress}): " +
                                $"X: {controllerState.GyroX,8:F2}, " +
                                $"Y: {controllerState.GyroY,8:F2}, " +
                                $"Z: {controllerState.GyroZ,8:F2}");

                // --- New logic to determine and print dominant movement ---
                const float movementThreshold = 8.0f; // Ignore small jitters

                float absX = Math.Abs(controllerState.GyroX);
                float absY = Math.Abs(controllerState.GyroY);
                float absZ = Math.Abs(controllerState.GyroZ);

                string dominantMovement = "Still";

                // Check if any movement exceeds the threshold
                if (absX > movementThreshold || absY > movementThreshold || absZ > movementThreshold)
                {
                    // Determine which axis has the largest rotation speed
                    if (absX >= absY && absX >= absZ) // Pitch is dominant
                    {
                        dominantMovement = controllerState.GyroX > 0 ? "Tilting UP" : "Tilting DOWN";
                    }
                    else if (absY > absX && absY >= absZ) // Yaw is dominant
                    {
                        dominantMovement = controllerState.GyroY > 0 ? "Turning RIGHT" : "Turning LEFT";
                    }
                    else // Roll is dominant
                    {
                        dominantMovement = controllerState.GyroZ > 0 ? "Banking RIGHT" : "Banking LEFT";
                    }
                }
                
                Debug.WriteLine($"                  -> Dominant Action: {dominantMovement}");
            }
        }
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
            // Subscribe to all controller events
            controller.StateUpdated += OnControllerStateUpdated;
            controller.Disconnected += OnControllerDisconnected;
            controller.BatteryLevelUpdated += OnBatteryLevelUpdated;

            if (await controller.InitializeAsync())
            {
                var mapper = new VigemMapper(controller, _vigemClient);
                mapper.Connect();

                lock (_lock)
                {
                    // Double-check it wasn't added by a racing thread
                    if (_controllers.ContainsKey(controller.BluetoothAddress))
                    {
                        mapper.Dispose();
                        // Unsubscribe from all events to prevent memory leaks
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

    private void OnControllerStateUpdated(IController controller, ControllerState state)
    {
        lock (_lock)
        {
            // Store the latest state for the console print timer
            _latestStates[controller.BluetoothAddress] = state;
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
            _latestStates.Remove(address); // Remove from our state cache

            if (_controllers.Remove(address, out var storedController))
            {
                // Unsubscribe from all events to prevent memory leaks
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
        _consolePrintTimer.Dispose();
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
            _latestStates.Clear();
        }

        _vigemClient.Dispose();
    }
}