# Poké Ball Plus for Windows

A lightweight Windows utility that allows you to use your Nintendo Poké Ball Plus controller as a standard X-Input device on your PC.

![Poké Ball Plus Controller](https://github.com/user-attachments/assets/4568f462-1f7e-49bf-8076-8054346140be)

## Features

*   **Seamless Integration**: Connects to your Poké Ball Plus via Bluetooth LE.
*   **Standard X-Input**: Presents the controller to Windows as a standard X-Input device, ensuring compatibility with most modern games.
*   **Multi-Controller Support**: Connect and manage multiple Poké Ball Plus controllers on the same PC.
*   **Battery Level Monitoring**: See the current battery level of each connected controller at a glance.
*   **System Tray Management**: Runs quietly in the system tray, showing connection status and battery levels.
*   **Automatic Reconnection**: Automatically detects and connects to paired controllers.

## Input Mapping

This application maps the Poké Ball Plus controls to a standard X-Input layout. Not all X-Input functions are covered, as the Poké Ball Plus has a limited number of inputs.

| Poké Ball Plus Input | X-Input Control                | Status      |
| :------------------- | :------------------------------| :---------- |
| **Joystick**         | **Left Analog Stick**          | ✅ Supported |
| Top Button           | A Button                       | ✅ Supported |
| Joystick Click       | B Button                       | ✅ Supported |
| Shake / Gyro         | *Right Analog Stick / gyro*    | 🚧 Planned  |

## Current Limitations

Due to the proprietary nature of the Poké Ball Plus, some features are not accessible with today's open-source knowledge. This application **cannot**:

*   Control the rumble/vibration motor.
*   Control the color or pattern of the LED light.
*   Play sounds through the built-in speaker.
*   Read or write the Pokémon data stored inside the controller.

**Important**: This application only reads input data and does **not** attempt to write any data to the controller. It is safe to use and will not harm your device or the data on it.

## Getting Started

Follow these steps to get your Poké Ball Plus working on your PC.

### Prerequisites

1.  A **Poké Ball Plus** controller.
2.  **Windows 10** (or newer) with Bluetooth support.
3.  The **ViGEmBus Driver**.

### Installation & Setup Guide

1.  **Download the Application**
    *   Grab the latest version from the **[Releases Page](https://github.com/rna0/pokeball-plus-4-windows/releases)**.

2.  **Install the ViGEmBus Driver**
    *   This project requires the ViGEmBus driver to create a virtual X-Input controller.
    *   Download and run the latest installer from the official [**ViGEmBus Releases Page**](https://github.com/nefarius/ViGEmBus/releases).

3.  **Pair Your Poké Ball Plus**
    *   Open the Windows Bluetooth settings (`Settings` -> `Bluetooth & devices` -> `Add device`).
    *   Press and hold the **joystick button** on your Poké Ball Plus until it starts to vibrate and the light flashes white.
    *   Select the "Poké Ball Plus" from the list of available devices to pair it with your PC.

4.  **Run the Application**
    *   Unzip the `PokeballPlus4Windows` file you downloaded in Step 1.
    *   Run `PokeballPlus4Windows.exe`.
    *   The application will start in your system tray. It will automatically scan for, connect to, and map your paired Poké Ball Plus.

Your controller is now ready! It will be recognized by Windows and games as a standard X-Input controller.

## Future Plans

*   **Gyro Support**: Implement motion controls to map the Poké Ball's gyroscope to the right analog stick, enabling camera control in many games.

## How It Works

This application continuously scans for paired Bluetooth LE devices that match the Poké Ball Plus. Once a device is found, it establishes a connection to read the input data for the joystick and buttons. This data is then translated and fed into a virtual X-Input controller created by the ViGEmBus driver, making it universally compatible with games that support XInput.

## Acknowledgements

This project stands on the shoulders of giants. A huge thank you to the creators and maintainers of these incredible open-source projects:

*   **Nefarius's ViGEm (Virtual Gamepad Emulation Framework)**: The core framework that makes gamepad emulation possible.
*   **Avalonia UI**: A powerful, cross-platform UI framework used for the application's interface.

## License

This project is licensed under the MIT License. See the `LICENSE` file for details.
