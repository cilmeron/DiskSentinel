using System.Diagnostics;
using System.Text.Json;

namespace DiskSentinel;

public sealed record SmartAttribute(string Id, string Name, string Value, string Worst, string Threshold, string Raw, bool Failed);

public sealed record Problem(string Kind, string Message);

public sealed class DiskInfo
{
    public const string Deg = "\u00B0C";

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

    public List<(string Key, string Value)> Info { get; } = new();
    public List<SmartAttribute> Attributes { get; } = new();

    /// <summary>0-100 health score (null when the drive could not be read).</summary>
    public int? Health { get; private set; }

    /// <summary>Informational notes shown next to the health score.</summary>
    public List<string> Notes { get; } = new();

    /// <summary>Things that need the user's attention (these raise alerts).</summary>
    public List<Problem> Problems { get; } = new();

    /// <summary>Estimated days of life left, extrapolating the average wear rate so far.</summary>
    public double? RemainingDays
    {
        get
        {
            if (PowerOnHours is not > 0 || LifeLeft is not int left) return null;
            if (left <= 0) return 0;
            double used = 100 - left;
            if (used <= 0) return null; // no measurable wear yet
            return PowerOnHours.Value / 24.0 * left / used;
        }
    }

    public void Evaluate()
    {
        Notes.Clear();
        Problems.Clear();
        if (PowerOnHours is null && Temp is null)
        {
            Health = null;
            Notes.Add("S.M.A.R.T. data could not be read for this drive.");
            return;
        }

        int h = 100;
        if (LifeLeft is int left)
        {
            h = Math.Min(h, left);
            Notes.Add($"Wear: {100 - left}% of the drive's rated life has been used.");
        }
        if (SmartPassed == false)
        {
            h = Math.Min(h, 10);
            Problems.Add(new("smart", "The drive failed its S.M.A.R.T. self-assessment. Back up your data now."));
        }
        if (Reallocated is > 0)
        {
            h -= (int)Math.Min(30, Reallocated.Value);
            Notes.Add($"{Reallocated} reallocated sectors.");
        }
        if (Pending is > 0)
        {
            h -= (int)Math.Min(30, Pending.Value);
            Problems.Add(new("pending", $"{Pending} sectors are pending reallocation (possible failing surface)."));
        }
        if (MediaErrors is > 0)
        {
            h -= (int)Math.Min(30, MediaErrors.Value);
            Problems.Add(new("media", $"{MediaErrors} media/data integrity errors were logged."));
        }
        if (Spare is < 20)
        {
            h = Math.Min(h, 40);
            Problems.Add(new("spare", $"Available spare capacity is down to {Spare}%."));
        }
        if (Temp >= Settings.TempCritical)
            Problems.Add(new("temp", $"Temperature is {Temp}{Deg}. Check cooling."));
        if (RemainingDays is double days && days < Settings.RemainingLifeWarningDays)
            Problems.Add(new("life", $"Estimated remaining life is {FormatSpan(days)}. Back up your data and plan to replace this drive."));

        Health = Math.Max(0, h);
    }

    public string Verdict
    {
        get
        {
            if (Health is null) return "Unknown";
            if (Problems.Count > 0) return "Attention needed";
            return Health >= 70 ? "Good" : "Good - ageing, no action needed yet";
        }
    }

    public string StatusText
    {
        get
        {
            var lines = new List<string>();
            if (Health is null) { lines.AddRange(Notes); return string.Join("\n", lines); }
            lines.Add(Problems.Count == 0
                ? $"The status of the disk is {(Health >= 70 ? "good" : "acceptable")}. No backup or replacement is needed yet."
                : "Attention needed:");
            lines.AddRange(Problems.Select(p => "- " + p.Message));
            lines.AddRange(Notes);
            return string.Join("\n", lines);
        }
    }

    public static string FormatSpan(double days)
    {
        int total = (int)Math.Round(days);
        int years = total / 365;
        int months = total % 365 / 30;
        int d = total % 365 % 30;
        var parts = new List<string>();
        if (years > 0) parts.Add($"{years} year{(years == 1 ? "" : "s")}");
        if (months > 0) parts.Add($"{months} month{(months == 1 ? "" : "s")}");
        if (d > 0 || parts.Count == 0) parts.Add($"{d} day{(d == 1 ? "" : "s")}");
        return string.Join(", ", parts);
    }

    public string RemainingText => RemainingDays is double d
        ? FormatSpan(d)
        : LifeLeft == 100 ? "No measurable wear yet" : "n/a";

    public string PowerOnText => PowerOnHours is long p ? $"{p / 24:N0} days, {p % 24} hours" : "n/a";

    public string ToolTip()
    {
        string t = Temp is int c ? $"{c}{Deg}" : "n/a";
        string h = Health is int v ? $"{v}%" : "n/a";
        string s = $"{Name}\nTemp {t} \u00B7 Health {h}";
        return s.Length > 127 ? s[..127] : s;
    }

    public string DetailText() =>
        $"{Name}  ({(SizeBytes is long b ? $"{b / 1e9:N0} GB" : "?")})\n" +
        $"Health        : {(Health is int h ? $"{h}%" : "n/a")} - {Verdict}\n" +
        $"Temperature   : {(Temp is int t ? $"{t} {Deg}" : "n/a")}\n" +
        $"Power-on time : {PowerOnText}\n" +
        $"Remaining life: {RemainingText}\n" +
        $"Lifetime writes: {(WrittenBytes is double w ? $"{w / 1e12:N2} TB" : "n/a")}\n" +
        $"Serial        : {Serial}\n";
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
            candidates.Add(System.IO.Path.Combine(dir.Trim(), "smartctl.exe"));
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

        // ---- Information tab ----
        void Info(string key, string? value) { if (!string.IsNullOrEmpty(value)) d.Info.Add((key, value)); }
        Info("Model", d.Name);
        Info("Serial number", d.Serial);
        Info("Firmware", Str(j, "firmware_version"));
        if (d.SizeBytes is long cap) Info("Capacity", $"{cap / 1e9:N1} GB ({cap:N0} bytes)");
        Info("Interface", Str(At(j, "device") ?? default, "protocol"));
        Info("Device path", device);
        if (j.TryGetProperty("rotation_rate", out var rr) && rr.ValueKind == JsonValueKind.Number)
            Info("Type", rr.GetInt32() == 0 ? "Solid state drive" : $"Hard disk, {rr.GetInt32()} rpm");
        else if (j.TryGetProperty("nvme_version", out _)) Info("Type", "NVMe solid state drive");
        Info("Form factor", Str(At(j, "form_factor") ?? default, "name"));
        Info("SATA version", Str(At(j, "sata_version") ?? default, "string"));
        Info("NVMe version", Str(At(j, "nvme_version") ?? default, "string"));
        Info("Link speed", Str(At(j, "interface_speed", "current") ?? default, "string"));
        if (At(j, "trim", "supported") is { ValueKind: JsonValueKind.True or JsonValueKind.False } trim)
            Info("TRIM supported", trim.GetBoolean() ? "Yes" : "No");
        if (Num(At(j, "logical_block_size")) is long lbs) Info("Logical block size", $"{lbs} bytes");

        if (j.TryGetProperty("nvme_smart_health_information_log", out var nv))
        {
            if (Num(At(nv, "percentage_used")) is long used) d.LifeLeft = (int)Math.Clamp(100 - used, 0, 100);
            d.Spare = (int?)Num(At(nv, "available_spare"));
            if (Num(At(nv, "data_units_written")) is long units) d.WrittenBytes = units * 512_000.0;
            d.MediaErrors = Num(At(nv, "media_errors"));
            d.PowerCycles = Num(At(nv, "power_cycles"));
            d.UnsafeShutdowns = Num(At(nv, "unsafe_shutdowns"));

            foreach (var prop in nv.EnumerateObject())
            {
                if (prop.Value.ValueKind != JsonValueKind.Number) continue;
                string nice = char.ToUpper(prop.Name[0]) + prop.Name[1..].Replace('_', ' ');
                d.Attributes.Add(new SmartAttribute("", nice, "", "", "", prop.Value.GetRawText(), false));
            }
        }
        else if (At(j, "ata_smart_attributes", "table") is { ValueKind: JsonValueKind.Array } table)
        {
            var attrs = new Dictionary<int, JsonElement>();
            foreach (var a in table.EnumerateArray())
            {
                if (Num(At(a, "id")) is long id) attrs[(int)id] = a;
                long value = Num(At(a, "value")) ?? 0, thresh = Num(At(a, "thresh")) ?? 0;
                d.Attributes.Add(new SmartAttribute(
                    $"{Num(At(a, "id"))}", Str(a, "name")?.Replace('_', ' ') ?? "",
                    $"{value}", $"{Num(At(a, "worst"))}", $"{thresh}",
                    Str(At(a, "raw") ?? default, "string") ?? "", thresh > 0 && value <= thresh));
            }

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
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() : null;
}
