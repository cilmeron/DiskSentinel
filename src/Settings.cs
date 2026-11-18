namespace DiskSentinel;

public static class Settings
{
    /// <summary>How often drives are re-read.</summary>
    public const int RefreshSeconds = 60;

    /// <summary>A temperature sample is stored in the history at most this often, per drive.</summary>
    public const int HistoryIntervalSeconds = 300;

    /// <summary>Tray icon turns orange at this temperature (degrees C).</summary>
    public const int TempWarning = 55;

    /// <summary>Tray icon turns red and an alert is shown at this temperature (degrees C).</summary>
    public const int TempCritical = 65;

    /// <summary>
    /// A backup/replace warning is only raised when the estimated remaining life
    /// drops below this many days (about 6 months).
    /// </summary>
    public const int RemainingLifeWarningDays = 183;
}
