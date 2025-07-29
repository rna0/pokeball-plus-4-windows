using System.Collections.Generic;

namespace PokeballPlus4Windows;

/// <summary>
/// Holds information about a single connected controller.
/// </summary>
/// <param name="Address">The unique Bluetooth address of the controller.</param>
/// <param name="BatteryLevel">The last known battery level (0-100), or null if not yet read.</param>
public record ControllerInfo(ulong Address, byte? BatteryLevel);

/// <summary>
/// Represents the overall state of the driver, including all connected controllers.
/// </summary>
/// <param name="TooltipText">The text to display in the tray icon's tooltip.</param>
/// <param name="IsConnected">True if at least one controller is connected.</param>
/// <param name="Controllers">A list of all currently connected controllers.</param>
public record DriverStatus(string TooltipText, bool IsConnected, IReadOnlyList<ControllerInfo> Controllers);