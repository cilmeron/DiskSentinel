using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace DiskSentinel;

public sealed class DiskInfo
{
    public string Name { get; set; } = "";
    public string Serial { get; set; } = "";
    public string Device { get; set; } = "";
    public long? SizeBytes { get; set; }
    public int? Temp { get; set; }
    public long? PowerOnHours { get; set; }
    public bool? SmartPassed { get; set; }
    public int? LifeLeft { get; set; }
    public int? Spare { get; set; }
    public double? WrittenBytes { get; set; }
    public long? MediaErrors { get; set; }
    public long? Reallocated { get; set; }
    public long? Pending { get; set; }
    public long? PowerCycles { get; set; }
    public long? UnsafeShutdowns { get; set; }

    public int? Health { get; private set; }
    public List<string> Reasons { get; } = new();

    /// <summary>Computes the 0-100 health score and the reasons behind it.</summary>
    public void Evaluate()
    {
        Reasons.Clear();
        if (PowerOnHours is null && Temp is null)
        {
            Health = null;
            Reasons.Add("S.M.A.R.T. data could not be read for this drive.");
            return;
        }

        int h = 100;
        if (LifeLeft is int left)
        {
            h = Math.Min(h, left);
            Reasons.Add($"Wear: {100 - left}% of rated life used");
        }
        if (SmartPassed == false)
        {
            h = Math.Min(h, 10);
            Reasons.Add("S.M.A.R.T. overall self-assessment FAILED");
        }
        long bad = (Reallocated ?? 0) + (Pending ?? 0);
        if (bad > 0)
        {
            h -= (int)Math.Min(30, bad);
            Reasons.Add($"{Reallocated ?? 0} reallocated / {Pending ?? 0} pending sectors");
        }
        if (MediaErrors is > 0)
        {
            h -= (int)Math.Min(30, MediaErrors.Value);
            Reasons.Add($"{MediaErrors} media errors");
        }
        if (Spare is < 20)
        {
            h = Math.Min(h, 40);
            Reasons.Add($"Available spare only {Spare}%");
        }
        if (Temp >= Settings.TempCritical)
        {
            h -= 15;
            Reasons.Add($"Temperature {Temp}Â°C is critical");
        }
        Health = Math.Max(0, h);
    }

    public string Verdict => Health switch
    {
        null => "Unknown",
        >= 70 => "Good",
        >= Settings.HealthWarning => "Fair - keep an eye on it",
        _ => "Poor - back up your data",
    };

    public string EstimatedLifeLeft()
    {
        if (PowerOnHours is not > 0 || LifeLeft is not (> 0 and < 100)) return "n/a";
        double used = 100 - LifeLeft.Value;
        double days = PowerOnHours.Value / 24.0 * LifeLeft.Value / used;
        return $"~{days:N0} days at the average wear rate so far";
    }

    public string ToolTip()
    {
        string t = Temp is int c ? $"{c}Â°C" : "n/a";
        string h = Health is int v ? $"{v}%" : "n/a";
        string s = $"{Name}\nTemp {t} Â· Health {h}";
        return s.Length > 127 ? s[..127] : s;
    }

    public string DetailText()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{Name}  ({(SizeBytes is long b ? $"{b / 1e9:N0} GB" : "?")})");
        sb.AppendLine($"Health        : {(Health is int h ? $"{h}%" : "n/a")}  - {Verdict}");
        foreach (var r in Reasons) sb.AppendLine($"                {r}");
        sb.AppendLine($"Temperature   : {(Temp is int t ? $"{t} Â°C" : "n/a")}");
        sb.AppendLine($"Power-on time : {(PowerOnHours is long p ? $"{p / 24:N0} days, {p % 24} hours" : "n/a")}");
        sb.AppendLine($"Lifetime writes: {(WrittenBytes is double w ? $"{w / 1e12:N2} TB" : "n/a")}");
        sb.AppendLine($"Power cycles  : {PowerCycles?.ToString() ?? "n/a"}   (unsafe shutdowns: {UnsafeShutdowns?.ToString() ?? "n/a"})");
        sb.AppendLine($"Est. life left: {EstimatedLifeLeft()}");
        sb.AppendLine($"Serial        : {Serial}");
        return sb.ToString();
    }
}

/// <summary>Reads drive data through smartctl (smartmontools) and its JSON output.</summary>
public static class SmartService
{
    static readonly string? SmartctlPath = FindSmartctl();

    public static bool Available => SmartctlPath is not null;

    static string? FindSmartctl()
    {
        var candidates = new List<string>();
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
            candidates.Add(Path.Combine(dir.Trim(), "smartctl.exe"));
        candidates.Add(@"C:\Program Files\smartmontools\bin\smartctl.exe");
        candidates.Add(@"C:\Program Files (x86)\smartmontools\bin\smartctl.exe");
        return candidates.FirstOrDefault(File.Exists);
    }

    static JsonDocument? Run(string arguments)
    {
        if (SmartctlPath is null) return null;
        var psi = new ProcessStartInfo(SmartctlPath, "-j " + arguments)
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var p = Process.Start(psi);
        if (p is null) return null;
        string output = p.StandardOutput.ReadToEnd();
        if (!p.WaitForExit(60_000)) { try { p.Kill(); } catch { } }
        try { return JsonDocument.Parse(output); } catch (JsonException) { return null; }
    }

    public static List<DiskInfo> ReadAll()
    {
        var result = new List<DiskInfo>();
        using var scan = Run("--scan");
        if (scan is null || !scan.RootElement.TryGetProperty("devices", out var devices)) return result;

        foreach (var dev in devices.EnumerateArray())
        {
            string name = dev.GetProperty("name").GetString() ?? "";
            string type = dev.GetProperty("type").GetString() ?? "auto";
            // Windows reports SATA drives as "scsi"; smartctl needs "sat" to read them.
            var types = type == "scsi" ? new[] { type, "sat" } : new[] { type };
            foreach (var t in types)
            {
                using var doc = Run($"-a {name} -d {t}");
                if (doc is not null && doc.RootElement.TryGetProperty("model_name", out _))
                {
                    var info = Parse(doc.RootElement, name);
                    info.Evaluate();
                    // Skip optical drives (no data at all) and drives already seen via another
                    // device path (e.g. a RAID/CSMI alias of the same disk).
                    bool noData = info.Temp is null && info.PowerOnHours is null && info.SizeBytes is null;
                    bool duplicate = info.Serial != "" && result.Any(r => r.Serial == info.Serial);
                    if (!noData && !duplicate) result.Add(info);
                    break;
                }
            }
        }
        return result;
    }

    static DiskInfo Parse(JsonElement j, string device)
    {
        var d = new DiskInfo
        {
            Device = device,
            Name = Str(j, "model_name") ?? device,
            Serial = Str(j, "serial_number") ?? "",
            SizeBytes = Num(At(j, "user_capacity", "bytes")),
            Temp = (int?)Num(At(j, "temperature", "current")),
            PowerOnHours = Num(At(j, "power_on_time", "hours")),
            SmartPassed = At(j, "smart_status", "passed") is { ValueKind: JsonValueKind.True or JsonValueKind.False } p
                ? p.GetBoolean() : null,
        };

        if (j.TryGetProperty("nvme_smart_health_information_log", out var nv))
        {
            if (Num(At(nv, "percentage_used")) is long used) d.LifeLeft = (int)Math.Clamp(100 - used, 0, 100);
            d.Spare = (int?)Num(At(nv, "available_spare"));
            if (Num(At(nv, "data_units_written")) is long units) d.WrittenBytes = units * 512_000.0;
            d.MediaErrors = Num(At(nv, "media_errors"));
            d.PowerCycles = Num(At(nv, "power_cycles"));
            d.UnsafeShutdowns = Num(At(nv, "unsafe_shutdowns"));
        }
        else if (At(j, "ata_smart_attributes", "table") is { ValueKind: JsonValueKind.Array } table)
        {
            var attrs = new Dictionary<int, JsonElement>();
            foreach (var a in table.EnumerateArray())
                if (Num(At(a, "id")) is long id) attrs[(int)id] = a;

            // Vendor "life remaining" style attributes (normalized value counts down from 100).
            foreach (int id in new[] { 202, 231, 233, 177 })
                if (attrs.TryGetValue(id, out var a) && Num(At(a, "value")) is > 0 and <= 100 and long v)
                { d.LifeLeft = (int)v; break; }

            // Total LBAs written, in 512-byte units.
            foreach (int id in new[] { 246, 241 })
                if (attrs.TryGetValue(id, out var a) && Num(At(a, "raw", "value")) is long lbas)
                { d.WrittenBytes = lbas * 512.0; break; }

            long? Raw(int id) => attrs.TryGetValue(id, out var a) ? Num(At(a, "raw", "value")) : null;
            d.Reallocated = Raw(5);
            d.Pending = Raw(197);
            d.PowerCycles = Raw(12);
            d.PowerOnHours ??= Raw(9);
        }
        return d;
    }

    static JsonElement? At(JsonElement e, params string[] path)
    {
        JsonElement cur = e;
        foreach (var p in path)
            if (cur.ValueKind != JsonValueKind.Object || !cur.TryGetProperty(p, out cur)) return null;
        return cur;
    }

    static long? Num(JsonElement? e) =>
        e is { ValueKind: JsonValueKind.Number } n && n.TryGetInt64(out long v) ? v : null;

    static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}

