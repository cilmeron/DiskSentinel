using System.Globalization;

namespace DiskSentinel;

public sealed record Sample(DateTime Time, string Serial, int? Temp, int? Health, long? PowerOnHours);

/// <summary>Persists temperature/health samples (CSV) and a small event log under ProgramData.</summary>
public sealed class HistoryStore
{
    // DISKSENTINEL_DATA overrides the data folder (used for testing).
    public static string DataDir => Dir;
    static readonly string Dir = Environment.GetEnvironmentVariable("DISKSENTINEL_DATA") ?? System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "DiskSentinel");
    static readonly string HistoryFile = System.IO.Path.Combine(Dir, "history.csv");
    static readonly string EventFile = System.IO.Path.Combine(Dir, "events.log");

    readonly List<Sample> _samples = new();
    readonly Dictionary<string, DateTime> _lastStored = new();

    public HistoryStore()
    {
        try
        {
            if (!File.Exists(HistoryFile)) return;
            foreach (var line in File.ReadLines(HistoryFile))
            {
                var p = line.Split(',');
                if (p.Length < 5 || !long.TryParse(p[0], out long unix)) continue;
                var s = new Sample(DateTimeOffset.FromUnixTimeSeconds(unix).LocalDateTime, p[1],
                    Int(p[2]), Int(p[3]), long.TryParse(p[4], out long poh) ? poh : null);
                _samples.Add(s);
                _lastStored[s.Serial] = s.Time;
            }
        }
        catch (IOException) { }
    }

    static int? Int(string s) => int.TryParse(s, out int v) ? v : null;

    public IEnumerable<Sample> For(string serial) => _samples.Where(s => s.Serial == serial);

    public void Add(DiskInfo d)
    {
        var now = DateTime.Now;
        if (_lastStored.TryGetValue(d.Serial, out var last) && (now - last).TotalSeconds < Settings.HistoryIntervalSeconds) return;
        var s = new Sample(now, d.Serial, d.Temp, d.Health, d.PowerOnHours);
        _samples.Add(s);
        _lastStored[d.Serial] = now;
        try
        {
            Directory.CreateDirectory(Dir);
            File.AppendAllText(HistoryFile,
                $"{new DateTimeOffset(now).ToUnixTimeSeconds()},{d.Serial},{s.Temp},{s.Health},{s.PowerOnHours}\n");
        }
        catch (IOException) { }
    }

    // ---- event log ----

    public static void Log(string message)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.AppendAllText(EventFile,
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + "\t" + message + "\n");
        }
        catch (IOException) { }
    }

    public static List<(string Time, string Message)> ReadLog(int max = 500)
    {
        var result = new List<(string, string)>();
        try
        {
            if (!File.Exists(EventFile)) return result;
            foreach (var line in File.ReadLines(EventFile).Reverse().Take(max))
            {
                var p = line.Split('\t', 2);
                if (p.Length == 2) result.Add((p[0], p[1]));
            }
        }
        catch (IOException) { }
        return result;
    }
}
