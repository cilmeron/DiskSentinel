namespace DiskSentinel;

/// <summary>
/// Turns one-off readings into alerts only when a problem repeats:
/// <list type="bullet">
/// <item>error counters (pending/reallocated sectors, media errors) alert once they have grown on
/// at least <see cref="GrowthEvents"/> separate occasions, or reach <see cref="HardLimit"/>;</item>
/// <item>temperature alerts once it has been critical for <see cref="TempStreak"/> readings in a row.</item>
/// </list>
/// Counter baselines are persisted so a restart doesn't forget them.
/// </summary>
public sealed class CounterTracker
{
    public const int GrowthEvents = 2;
    public const long HardLimit = 10;
    public const int TempStreak = 3;

    static readonly string File_ = System.IO.Path.Combine(HistoryStore.DataDir, "counters.csv");

    sealed class State { public long Last; public int Growths; }

    readonly Dictionary<string, State> _counters = new();
    readonly Dictionary<string, int> _tempStreaks = new();

    public CounterTracker()
    {
        try
        {
            if (!File.Exists(File_)) return;
            foreach (var line in File.ReadLines(File_))
            {
                var p = line.Split(',');
                if (p.Length == 3 && long.TryParse(p[1], out long last) && int.TryParse(p[2], out int g))
                    _counters[p[0]] = new State { Last = last, Growths = g };
            }
        }
        catch (IOException) { }
    }

    /// <summary>Adds alert-worthy problems to the disk based on how its readings have changed.</summary>
    public void Apply(DiskInfo d)
    {
        bool changed = false;
        Check(d, "pending", d.Pending, "sectors keep becoming pending reallocation", ref changed);
        Check(d, "realloc", d.Reallocated, "sectors keep being reallocated", ref changed);
        Check(d, "media", d.MediaErrors, "media/data integrity errors keep occurring", ref changed);
        if (changed) Save();

        _tempStreaks.TryGetValue(d.Serial, out int streak);
        streak = d.Temp >= Settings.TempCritical ? streak + 1 : 0;
        _tempStreaks[d.Serial] = streak;
        if (streak >= TempStreak)
            d.Problems.Add(new("temp", $"Temperature has stayed at {d.Temp}{DiskInfo.Deg} or above. Check cooling."));
    }

    void Check(DiskInfo d, string kind, long? value, string what, ref bool changed)
    {
        if (value is not long v) return;
        string key = $"{d.Serial}|{kind}";
        if (!_counters.TryGetValue(key, out var s))
        {
            _counters[key] = new State { Last = v }; // first sighting is the baseline, not a "growth"
            changed = true;
            s = _counters[key];
        }
        else if (v > s.Last)
        {
            s.Growths++;
            s.Last = v;
            changed = true;
        }
        else if (v < s.Last)
        {
            s.Last = v;
            changed = true;
        }

        if (v > 0 && (s.Growths >= GrowthEvents || v >= HardLimit))
            d.Problems.Add(new(kind, $"{v} {what}."));
    }

    void Save()
    {
        try
        {
            Directory.CreateDirectory(HistoryStore.DataDir);
            File.WriteAllLines(File_, _counters.Select(kv => $"{kv.Key},{kv.Value.Last},{kv.Value.Growths}"));
        }
        catch (IOException) { }
    }
}
