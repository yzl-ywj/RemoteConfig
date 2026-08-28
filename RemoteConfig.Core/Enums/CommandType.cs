namespace RemoteConfig.Core.Enums;

/// <summary>
/// Types of commands that can be sent to devices.
/// </summary>
public enum CommandType
{
    /// <summary>Reboot the device.</summary>
    Reboot = 0,

    /// <summary>Restart a specific application/service on the device.</summary>
    RestartApp = 1,

    /// <summary>Toggle a GPIO pin (on/off).</summary>
    ToggleGpio = 2,

    /// <summary>Force a firmware check / OTA probe.</summary>
    CheckFirmware = 3,

    /// <summary>Factory reset the device.</summary>
    FactoryReset = 4,

    /// <summary>Collect diagnostic logs and upload.</summary>
    CollectLogs = 5,

    /// <summary>Custom command with free-form payload.</summary>
    Custom = 99
}
