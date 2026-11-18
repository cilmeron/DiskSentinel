namespace DiskSentinel;

public static class Settings
{
    /// <summary>How often drives are re-read.</summary>
    public const int RefreshSeconds = 60;

    /// <summary>Tray icon turns orange at this temperature (°C).</summary>
    public const int TempWarning = 55;

    /// <summary>Tray icon turns red and an alert is shown at this temperature (°C).</summary>
    public const int TempCritical = 65;

    /// <summary>Health percentage below which an alert is shown.</summary>
    public const int HealthWarning = 50;
}
