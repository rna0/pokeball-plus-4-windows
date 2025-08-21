using System;
using System.Diagnostics;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using PokeballPlus4Windows.Modularity;

namespace PokeballPlus4Windows;

/// <summary>
/// Represents a single connected Poké Ball Plus device.
/// </summary>
public sealed class PokeballController(BluetoothLEDevice device) : IController
{
    // --- Bluetooth UUIDs ---
    private static readonly Guid PokeballServiceUuid = new("6675e16c-f36d-4567-bb55-6b51e27a23e5");
    private static readonly Guid PokeballCharacteristicUuid = new("6675e16c-f36d-4567-bb55-6b51e27a23e6");
    private static readonly Guid BatteryServiceUuid = new("0000180f-0000-1000-8000-00805f9b34fb");
    private static readonly Guid BatteryLevelCharacteristicUuid = new("00002a19-0000-1000-8000-00805f9b34fb");

    // --- Axis Calibration & Deadzone ---
    private const float AxisRawMinX = 32f;
    private const float AxisRawMaxX = 192f;
    private const float AxisRawMinY = 36f;
    private const float AxisRawMaxY = 180f;
    private const float AnalogDeadzone = 0.075f;

    private readonly object _disposeLock = new();
    private bool _isDisposed;

    private GattCharacteristic? _inputCharacteristic;
    private GattCharacteristic? _batteryCharacteristic;

    public ulong BluetoothAddress => device.BluetoothAddress;

    public event Action<IController>? Disconnected;
    public event Action<IController, ControllerState>? StateUpdated;
    public event Action<IController, byte>? BatteryLevelUpdated;

    public async Task<bool> InitializeAsync()
    {
        try
        {
            device.ConnectionStatusChanged += OnConnectionStatusChanged;

            if (!await SubscribeToInputAsync())
            {
                return false;
            }

            _ = SubscribeToBatteryAsync();

            return true;
        }
        catch
        {
            return false;
        }
    }

    private void OnConnectionStatusChanged(BluetoothLEDevice sender, object args)
    {
        if (sender.ConnectionStatus == BluetoothConnectionStatus.Disconnected)
        {
            Disconnected?.Invoke(this);
        }
    }

    private async Task<bool> SubscribeToInputAsync()
    {
        var gattResult = await device.GetGattServicesForUuidAsync(PokeballServiceUuid, BluetoothCacheMode.Uncached);
        if (gattResult.Status != GattCommunicationStatus.Success || gattResult.Services.Count == 0) return false;

        var charResult = await gattResult.Services[0]
            .GetCharacteristicsForUuidAsync(PokeballCharacteristicUuid, BluetoothCacheMode.Uncached);
        if (charResult.Status != GattCommunicationStatus.Success || charResult.Characteristics.Count == 0) return false;

        _inputCharacteristic = charResult.Characteristics[0];
        _inputCharacteristic.ValueChanged += OnInputValueChanged;
        var status =
            await _inputCharacteristic.WriteClientCharacteristicConfigurationDescriptorAsync(
                GattClientCharacteristicConfigurationDescriptorValue.Notify);

        return status == GattCommunicationStatus.Success;
    }

    private async Task SubscribeToBatteryAsync()
    {
        try
        {
            var gattResult = await device.GetGattServicesForUuidAsync(BatteryServiceUuid, BluetoothCacheMode.Uncached);
            if (gattResult.Status != GattCommunicationStatus.Success || gattResult.Services.Count == 0) return;

            var charResult = await gattResult.Services[0]
                .GetCharacteristicsForUuidAsync(BatteryLevelCharacteristicUuid, BluetoothCacheMode.Uncached);
            if (charResult.Status != GattCommunicationStatus.Success || charResult.Characteristics.Count == 0) return;

            _batteryCharacteristic = charResult.Characteristics[0];

            var readResult = await _batteryCharacteristic.ReadValueAsync(BluetoothCacheMode.Uncached);
            if (readResult.Status == GattCommunicationStatus.Success)
            {
                OnBatteryValueChanged(readResult.Value.ToArray());
            }

            _batteryCharacteristic.ValueChanged += OnBatteryValueChanged;
            await _batteryCharacteristic.WriteClientCharacteristicConfigurationDescriptorAsync(
                GattClientCharacteristicConfigurationDescriptorValue.Notify);
        }
        catch { }
    }

    private void OnInputValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
    {
        ParseAndForwardInput(args.CharacteristicValue.ToArray());
    }

    private void OnBatteryValueChanged(GattCharacteristic? sender, GattValueChangedEventArgs args)
    {
        OnBatteryValueChanged(args.CharacteristicValue.ToArray());
    }

    private void OnBatteryValueChanged(byte[] data)
    {
        if (data.Length <= 0) return;
        var batteryLevel = data[0];
        BatteryLevelUpdated?.Invoke(this, batteryLevel);
    }

    private void ParseAndForwardInput(byte[] value)
    {
        // The full report with motion data is 17 bytes long.
        if (value.Length < 17) return;

        var state = new ControllerState
        {
            // --- Buttons and Stick ---
            ButtonA = (value[1] == 1 || value[1] == 3),
            ButtonB = (value[1] == 2 || value[1] == 3),
            AxisX = GetAnalogX(value[3], value[2]),
            AxisY = GetAnalogY(value[4]),

            // --- Gyroscope ---
            GyroX = GetGyroData(value[6], value[5]),
            GyroY = GetGyroData(value[8], value[7]),
            GyroZ = GetGyroData(value[10], value[9]),

            // --- Accelerometer ---
            AccelX = GetAccelData(value[12], value[11]),
            AccelY = GetAccelData(value[14], value[13]),
            AccelZ = GetAccelData(value[16], value[15])
        };

        StateUpdated?.Invoke(this, state);
    }

    #region Data Parsing Logic

    private static float MapAxisValue(float value, float a1, float a2, float b1, float b2)
    {
        value = Math.Clamp(value, a1, a2);
        return b1 + ((value - a1) * (b2 - b1)) / (a2 - a1);
    }

    private static float GetAnalogX(byte byte1, byte byte2)
    {
        byte value = (byte)(((byte1 & 0x0F) << 4) | ((byte2 >> 4) & 0x0F));
        float analogValue = MapAxisValue(value, AxisRawMinX, AxisRawMaxX, -1f, 1f);
        return Math.Abs(analogValue) < AnalogDeadzone ? 0f : analogValue;
    }

    private static float GetAnalogY(byte value)
    {
        // Inverting the axis to match standard controller behavior (up is positive)
        float analogValue = MapAxisValue(value, AxisRawMinY, AxisRawMaxY, 1f, -1f);
        return Math.Abs(analogValue) < AnalogDeadzone ? 0f : analogValue;
    }

    /// <summary>
    /// Decodes raw gyroscope data from two bytes.
    /// </summary>
    /// <remarks>
    /// This is a C# translation of the logic from the C++ 'Lighthouse' project.
    /// The data format is custom.
    /// </remarks>
    private static float GetGyroData(byte firstByte, byte secondByte)
    {
        // The high 4 bits of the first byte determine the sign.
        // If the most significant bit is 0 (value < 8), the number is negative.
        // If the most significant bit is 1 (value >= 8), the number is positive.
        byte firstNibble = (byte)(firstByte >> 4 & 0x0F);
        float sign = (firstNibble < 8) ? -1f : 1f;

        // The second byte represents the fractional part of the value.
        float subDeg = secondByte / 100f;
        float magnitude;

        if (sign > 0) // Positive value
        {
            // For positive values, the magnitude is calculated by inverting the first byte.
            byte difference = (byte)(0xFF - firstByte);
            magnitude = (difference == 0x00) ? 0 : (difference + subDeg);
        }
        else // Negative value
        {
            // For negative values, the magnitude is the first byte itself.
            magnitude = (firstByte == 0xFF) ? 0 : (firstByte + subDeg);
        }

        return magnitude * sign;
    }

    /// <summary>
    /// Decodes raw accelerometer data from two bytes.
    /// </summary>
    /// <remarks>
    /// This is a C# translation of the logic from the C++ 'Lighthouse' project.
    /// The logic is identical to GetGyroData, but with a final scaling factor.
    /// </remarks>
    private static float GetAccelData(byte firstByte, byte secondByte)
    {
        // The sign determination is identical to the gyroscope.
        byte firstNibble = (byte)(firstByte >> 4 & 0x0F);
        float sign = (firstNibble < 8) ? -1f : 1f;

        // The second byte is the fractional part.
        float subG = secondByte / 100f;
        float magnitude;

        if (sign > 0) // Positive value
        {
            byte difference = (byte)(0xFF - firstByte);
            magnitude = (difference == 0x00) ? 0 : (difference + subG);
        }
        else // Negative value
        {
            magnitude = (firstByte == 0xFF) ? 0 : (firstByte + subG);
        }

        return magnitude * sign;
    }

    #endregion

    public void Dispose()
    {
        lock (_disposeLock)
        {
            if (_isDisposed) return;
            _isDisposed = true;
        }

        try
        {
            device.ConnectionStatusChanged -= OnConnectionStatusChanged;
            if (_inputCharacteristic != null) _inputCharacteristic.ValueChanged -= OnInputValueChanged;
            if (_batteryCharacteristic != null) _batteryCharacteristic.ValueChanged -= OnBatteryValueChanged;
        }
        catch { }

        device.Dispose();
    }
}