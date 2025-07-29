using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Threading;
using Nefarius.ViGEm.Client.Exceptions;

namespace PokeballPlus4Windows;

public class App : Application
{
    private PokeballVigemDriver? _driver;
    private TrayIcon? _trayIcon;
    
    private readonly List<NativeMenuItem> _controllerMenuItems = [];
    private readonly NativeMenuItemSeparator _separator = new();
 
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            _trayIcon = new TrayIcon
            {
                Icon = new WindowIcon(
                    AssetLoader.Open(new Uri("avares://PokeballPlus4Windows/Assets/PokeBallPlus.ico"))),
                ToolTipText = "Poké Ball Driver",
                IsVisible = true
            };

            _trayIcon.Menu = new NativeMenu();
            var exitMenuItem = new NativeMenuItem("Exit");
            exitMenuItem.Click += (_, _) => desktop.Shutdown();
            _trayIcon.Menu.Items.Add(exitMenuItem);

            try
            {
                _driver = new PokeballVigemDriver();
                _driver.StatusUpdated += OnDriverStatusUpdated;
                _driver.Start();
            }
            catch (VigemBusNotFoundException)
            {
                ShowErrorDialog(
                    "ViGEmBus driver is not installed. This application cannot run without it.\n\nPlease install it from:\nhttps://github.com/nefarius/ViGEmBus/releases/latest");
                desktop.Shutdown();
                return;
            }
            catch (Exception ex)
            {
                ShowErrorDialog($"An unexpected error occurred: {ex.Message}");
                desktop.Shutdown();
                return;
            }

            // --- Handle Shutdown ---
            desktop.ShutdownRequested += OnShutdownRequested;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void OnDriverStatusUpdated(DriverStatus status)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_trayIcon?.Menu == null) return;

            _trayIcon.ToolTipText = status.TooltipText;

            // 1. Clear all previously added controller menu items
            foreach (var item in _controllerMenuItems)
            {
                _trayIcon.Menu.Items.Remove(item);
            }
            _controllerMenuItems.Clear();

            // 2. Manage the separator's visibility based on controller count
            bool hasControllers = status.Controllers.Count > 0;
            bool separatorExists = _trayIcon.Menu.Items.Contains(_separator);

            if (hasControllers && !separatorExists)
            {
                _trayIcon.Menu.Items.Insert(_trayIcon.Menu.Items.Count - 1, _separator);
            }
            else if (!hasControllers && separatorExists)
            {
                _trayIcon.Menu.Items.Remove(_separator);
            }

            // 3. Add new items for each currently connected controller
            // We insert them at the top of the menu.
            for (var i = 0; i < status.Controllers.Count; i++)
            {
                var controllerInfo = status.Controllers[i];
                var batteryText = controllerInfo.BatteryLevel.HasValue
                    ? $"{controllerInfo.BatteryLevel}%"
                    : "Reading...";

                var addressHex = controllerInfo.Address.ToString("X");
                var shortAddress = addressHex.Length > 4 ? addressHex.Substring(addressHex.Length - 4) : addressHex;

                var menuItem = new NativeMenuItem($"Poké Ball ({shortAddress}): {batteryText}")
                {
                    IsEnabled = false
                };

                _trayIcon.Menu.Items.Insert(i, menuItem);
                _controllerMenuItems.Add(menuItem);
            }
        });
    }

    private void OnShutdownRequested(object? sender, ShutdownRequestedEventArgs e)
    {
        _driver?.Dispose();
        if (_trayIcon != null)
        {
            _trayIcon.IsVisible = false;
            _trayIcon.Dispose();
        }
    }

    private void ShowErrorDialog(string message)
    {
        var window = new Window
        {
            Title = "Driver Error",
            Content = new TextBlock { Text = message, Margin = new Thickness(20) },
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Width = 400,
            Height = 200
        };
        window.Show();
    }
}