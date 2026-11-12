using System.Globalization;
using System.Text;
using Nba2k27Editor.Core.Model;
using Nba2k27Editor.Core.Editing;

internal static class StatManagerProbe
{
    /// <summary>Read-only: look for a native table owner in the game's registered data-manager slots.</summary>
    public static int NativeRoot(Dictionary<string, string> options)
    {
        var session = new PlayerSession("NBA2K27.exe", options.GetValueOrDefault("data-dir"));
        try
        {
            session.Connect();
            var model = session.Model;
            var memory = (Nba2k27Editor.Core.Memory.Win32Memory)session.Memory;
            long table = options.TryGetValue("at", out var tableText) ? Convert.ToInt64(tableText.Replace("0x", ""), 16)
                : SeasonGames.Locate(memory)?.Address ?? throw new InvalidOperationException("No native table");
            long slot = memory.BaseAddress + model.RecoveredRoots["GameDataStore"];
            Console.WriteLine($"pid={memory.Pid} table=0x{table:X} storeSlot=0x{slot:X}");
            var regions = memory.PrivateRegions();
            byte[] slots = memory.ReadBytes(slot - 1024, 2048);
            for (int offset = 0; offset < slots.Length; offset += 8)
            {
                long owner = BitConverter.ToInt64(slots, offset);
                if (!regions.Any(r => owner >= r.Base && owner + 512 <= r.Base + r.Size)) continue;
                byte[] fields;
                try { fields = memory.ReadBytes(owner, 512); }
                catch (InvalidOperationException) { continue; }
                for (int field = 0; field < fields.Length; field += 8)
                {
                    long pointer = BitConverter.ToInt64(fields, field);
                    if (pointer < table - 128 || pointer > table) continue;
                    Console.WriteLine($"slotRva=0x{slot - 1024 + offset - memory.BaseAddress:X} relativeStore={offset - 1024} "
                        + $"owner=0x{owner:X} field=+{field} pointer=0x{pointer:X} tableDelta={table - pointer}");
                    Console.WriteLine("ownerHead=" + Convert.ToHexString(fields, 0, 64));
                }
            }
            Console.WriteLine("writes=0");
            return 0;
        }
        finally { session.Close(); }
    }

    /// <summary>Read-only diagnostic: enumerate native table candidates without selecting the first match.</summary>
    public static int Candidates(Dictionary<string, string> options)
    {
        var session = new PlayerSession("NBA2K27.exe", options.GetValueOrDefault("data-dir"));
        try
        {
            session.Connect();
            var memory = (Nba2k27Editor.Core.Memory.Win32Memory)session.Memory;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            Console.WriteLine($"pid={memory.Pid}");
            foreach (var (regionBase, size) in memory.PrivateRegions())
            {
                if (size < SeasonGames.Stride * 32 || size > 256L << 20) continue;
                if (options.TryGetValue("near", out var nearText))
                {
                    long near = Convert.ToInt64(nearText.Replace("0x", ""), 16);
                    if (near < regionBase || near >= regionBase + size) continue;
                }
                byte[] bytes;
                try { bytes = memory.ReadBytes(regionBase, (int)size); }
                catch (InvalidOperationException) { continue; }
                bool Header(int at)
                {
                    if (at < 0 || at + SeasonGames.Stride > bytes.Length) return false;
                    uint code = BitConverter.ToUInt32(bytes, at);
                    uint played = BitConverter.ToUInt32(bytes, at + 4);
                    return (played == 0 || played == code) && BitConverter.ToInt32(bytes, at + 8) is > 0 and < 10000
                        && SeasonGames.Date(code) is not null;
                }
                for (int at = 0; at + 3 * SeasonGames.Stride <= bytes.Length; at += 4)
                {
                    if (!Header(at) || !Header(at + SeasonGames.Stride) || !Header(at + 2 * SeasonGames.Stride)) continue;
                    uint key = BitConverter.ToUInt32(bytes, at + 12);
                    if (BitConverter.ToUInt32(bytes, at + SeasonGames.Stride + 12) != key
                        || BitConverter.ToUInt32(bytes, at + 2 * SeasonGames.Stride + 12) != key) continue;
                    int count = 0, unplayed = 0;
                    while (Header(at + count * SeasonGames.Stride)
                        && BitConverter.ToUInt32(bytes, at + count * SeasonGames.Stride + 12) == key)
                    {
                        if (BitConverter.ToUInt32(bytes, at + count * SeasonGames.Stride + 4) == 0) unplayed++;
                        count++;
                    }
                    if (count >= 32)
                    {
                        int last = at + (count - 1) * SeasonGames.Stride;
                        Console.WriteLine($"table=0x{regionBase + at:X} count={count} unplayed={unplayed} key=0x{key:X8} "
                            + $"first={SeasonGames.Date(BitConverter.ToUInt32(bytes, at)):yyyy-MM-dd}/id{BitConverter.ToInt32(bytes, at + 8)} "
                            + $"last={SeasonGames.Date(BitConverter.ToUInt32(bytes, last)):yyyy-MM-dd}/id{BitConverter.ToInt32(bytes, last + 8)}");
                        int next = last + SeasonGames.Stride;
                        if (next + 24 <= bytes.Length) Console.WriteLine($"nextHeader={Convert.ToHexString(bytes, next, 24)}");
                    }
                    at += count * SeasonGames.Stride - 4;
                }
            }
            Console.WriteLine($"elapsedMs={watch.Elapsed.TotalMilliseconds:0.0} writes=0");
            return 0;
        }
        finally { session.Close(); }
    }

    /// <summary>Read-only: dump the start of the player stat manager table and the records it points to.</summary>
    public static int Run(Dictionary<string, string> options)
    {
        var session = new PlayerSession("NBA2K27.exe", options.GetValueOrDefault("data-dir"));
        try
        {
            session.Connect();
            var model = session.Model;
            long roster = model.ResolveBasePointerEntry(model.BasePointerEntry("RosterData"), stopAtNull: true);
            int count = BitConverter.ToInt32(model.Memory.ReadBytes(roster + 448, 4));
            long table = model.ResolveBasePointerEntry(model.BasePointerEntry("PlayerStatManagerData"), stopAtNull: true);
            int seasonCount = BitConverter.ToInt32(model.Memory.ReadBytes(roster + 464, 4));
            long seasonTable = model.ResolveBasePointerEntry(model.BasePointerEntry("PlayerSeasonStats"), stopAtNull: true);
            Console.WriteLine($"manager count={count} table=0x{table:X}; season count={seasonCount} table=0x{seasonTable:X}");
            int stride = int.Parse(options.GetValueOrDefault("stride", "0"), CultureInfo.InvariantCulture);
            if (stride == 0 && count > 1 && seasonTable > table) stride = (int)Math.Min(4096, (seasonTable - table) / count);
            Console.WriteLine($"stride guess={stride}");
            int rows = int.Parse(options.GetValueOrDefault("rows", "3"), CultureInfo.InvariantCulture);
            int length = int.Parse(options.GetValueOrDefault("length", stride > 0 ? stride.ToString(CultureInfo.InvariantCulture) : "256"), CultureInfo.InvariantCulture);
            int first = int.Parse(options.GetValueOrDefault("first", "0"), CultureInfo.InvariantCulture);
            for (int r = first; r < first + rows; r++)
            {
                long at = table + (long)r * Math.Max(stride, 1);
                byte[] bytes = model.Memory.ReadBytes(at, length);
                Console.WriteLine($"-- row {r} @0x{at:X}");
                for (int o = 0; o < bytes.Length; o += 16)
                {
                    var hex = string.Join(" ", bytes.Skip(o).Take(16).Select(b => b.ToString("x2")));
                    var u16 = string.Join(",", Enumerable.Range(0, Math.Min(8, (bytes.Length - o) / 2)).Select(i => BitConverter.ToUInt16(bytes, o + i * 2)));
                    Console.WriteLine($"{o,5}: {hex,-47}  u16[{u16}]");
                }
            }
            if (options.TryGetValue("player", out var who))
            {
                var items = session.Connect();
                var specs = session.Specs.Values.Where(spec => spec.IsSeasonBound
                    && FieldIo.NodeText(spec.Payload["selected_record_source"]?["selector_field"]) == "CURRENTYEARSTATID").ToArray();
                (int offset, FieldSpec spec)[] cols = new[] { "COMPACTYEAR", "GAMESPLAYED", "POINTS", "MINUTES", "ASSISTS", "TEAMID" }
                    .Select(name => specs.FirstOrDefault(s => s.Key.EndsWith("/" + name, StringComparison.Ordinal))).Where(s => s is not null)
                    .Select(s => (FieldIo.FieldOffset(model.FieldVersionPayload(s!.Entry.Field)), s!)).ToArray();
                foreach (var item in items.Where(i => i.Label.Contains(who, StringComparison.OrdinalIgnoreCase)).Take(2))
                {
                    Console.WriteLine($"== {item.Label} #{item.Index} addr=0x{item.Address:X}");
                    var hkeys = session.Specs.Keys.Where(k => k.StartsWith("Stats/History/GAMESCOREHISTORY", StringComparison.Ordinal)).OrderBy(k => k).ToArray();
                    var hist = session.ReadPlayer(item, hkeys);
                    Console.WriteLine("  history: " + string.Join(" ", hkeys.Select(k => $"{k.Replace("Stats/History/GAMESCOREHISTORY", "")}={hist.Values[k].Raw}")));
                    foreach (var sel in new[] { "CURRENTYEARSTATID" }.Concat(Enumerable.Range(1, 32).Select(i => $"STATSID{i}")))
                    {
                        var field = model.FieldByName("Players", sel);
                        if (field is null) continue;
                        int id = Conversions.ToInt(model.ReadFieldAt("Players", field.Section, field.Field, item.Address).RawValue);
                        if (id is 0 or 65535) continue;
                        long rec = seasonTable + id * 64L;
                        var parts = cols.Select(c =>
                        {
                            var payload = model.FieldVersionPayload(c.spec.Entry.Field);
                            var raw = FieldIo.ReadAuthoredValue(model.Memory, rec + c.offset, payload);
                            return $"{c.spec.Key.Split('/').Last()}={FieldIo.RawToDisplayValue(c.spec.Entry.Section, c.spec.Entry.Field, payload, raw)}";
                        });
                        Console.WriteLine($"  {sel,-18} id={id,5} " + string.Join(" ", parts));
                    }
                }
            }
            Console.WriteLine("writes=0");
            return 0;
        }
        finally
        {
            session.Close();
        }
    }
    /// <summary>Read-only: follow each team's retired-jersey list (team +3848, next at +0, 120-byte records).</summary>
    public static int Jerseys(Dictionary<string, string> options)
    {
        var session = new PlayerSession("NBA2K27.exe", options.GetValueOrDefault("data-dir"));
        try
        {
            session.Connect();
            var model = session.Model;
            try
            {
                var cfg = model.BasePointerEntry("RetiredJerseyData");
                long tableAddr = model.ResolveBasePointerEntry(cfg, stopAtNull: true);
                Console.WriteLine($"RetiredJerseyData resolve=0x{tableAddr:X} cfg={cfg.ToJsonString()}");
                var mcfg = model.BasePointerEntry("RetiredJerseyManagerData");
                Console.WriteLine($"Manager resolve=0x{model.ResolveBasePointerEntry(mcfg, stopAtNull: true):X}");
                long roster = model.ResolveBasePointerEntry(model.BasePointerEntry("RosterData"), stopAtNull: true);
                int rc = BitConverter.ToInt32(model.Memory.ReadBytes(roster + 1104, 4));
                long rp = BitConverter.ToInt64(model.Memory.ReadBytes(roster + 1112, 8));
                Console.WriteLine($"roster+1104 count={rc} roster+1112 ptr=0x{rp:X} stride={model.StrideValue("retiredJerseySize")}");
            }
            catch (Exception exc) { Console.WriteLine("resolve failed: " + exc.Message); }
            var teams = model.ScanRecords("Teams").Take(int.Parse(options.GetValueOrDefault("teams", "31"), CultureInfo.InvariantCulture)).ToArray();
            int total = 0;
            foreach (var team in teams)
            {
                long node = BitConverter.ToInt64(model.Memory.ReadBytes(team.Address + 3848, 8));
                var parts = new List<string>();
                var seen = new HashSet<long>();
                while (node != 0 && seen.Add(node) && parts.Count < 40)
                {
                    byte[] b;
                    try { b = model.Memory.ReadBytes(node, 120); } catch (InvalidOperationException) { parts.Add($"<bad 0x{node:X}>"); break; }
                    long owner = BitConverter.ToInt64(b, 8);
                    int number = BitConverter.ToUInt16(b, 18);
                    int year = BitConverter.ToUInt16(b, 20);
                    string first = Encoding.Latin1.GetString(b, 56, 20).Split('\0')[0];
                    string last = Encoding.Latin1.GetString(b, 76, 20).Split('\0')[0];
                    string text = Encoding.Latin1.GetString(b, 96, 20).Split('\0')[0];
                    int tenures = (BitConverter.ToInt32(b, 20) >> 16) & 15;
                    parts.Add($"#{number}('{text}') {first} {last} {year} ten={tenures} {BitConverter.ToUInt16(b, 24)}-{BitConverter.ToUInt16(b, 26)}{(owner == team.Address ? "" : " owner!=team")}");
                    node = BitConverter.ToInt64(b, 0);
                }
                total += parts.Count;
                if (parts.Count > 0 || options.ContainsKey("all")) Console.WriteLine($"{team.Index} {team.Label} ptr=0x{BitConverter.ToInt64(model.Memory.ReadBytes(team.Address + 3848, 8)):X}: " + string.Join(" | ", parts));
            }
            Console.WriteLine($"teams={teams.Length} jerseys={total} writes=0");
            return 0;
        }
        finally
        {
            session.Close();
        }
    }
    /// <summary>Read-only: find a 32-bit value in all private memory and dump context around each hit.</summary>
    public static int FindValue(Dictionary<string, string> options)
    {
        uint value = uint.Parse(options["value"], CultureInfo.InvariantCulture);
        int before = int.Parse(options.GetValueOrDefault("before", "64"), CultureInfo.InvariantCulture);
        int after = int.Parse(options.GetValueOrDefault("after", "128"), CultureInfo.InvariantCulture);
        int limit = int.Parse(options.GetValueOrDefault("limit", "40"), CultureInfo.InvariantCulture);
        var session = new PlayerSession("NBA2K27.exe", options.GetValueOrDefault("data-dir"));
        try
        {
            session.Connect();
            var memory = (Nba2k27Editor.Core.Memory.Win32Memory)session.Memory;
            var hits = new List<long>();
            byte[] needle = BitConverter.GetBytes(value);
            foreach (var (baseAddress, size) in memory.PrivateRegions())
            {
                for (long at = 0; at < size && hits.Count < limit; at += 1 << 22)
                {
                    int chunk = (int)Math.Min(1 << 22, size - at);
                    byte[] bytes;
                    try { bytes = memory.ReadBytes(baseAddress + at, chunk); } catch (InvalidOperationException) { continue; }
                    for (int i = 0; i + 4 <= bytes.Length; i += 4)
                    {
                        if (bytes[i] == needle[0] && bytes[i + 1] == needle[1] && bytes[i + 2] == needle[2] && bytes[i + 3] == needle[3]) hits.Add(baseAddress + at + i);
                    }
                }
            }
            Console.WriteLine($"hits={hits.Count}");
            foreach (long hit in hits)
            {
                Console.WriteLine($"-- 0x{hit:X}");
                if (options.ContainsKey("brief")) continue;
                byte[] around;
                try { around = memory.ReadBytes(hit - before, before + after); } catch (InvalidOperationException) { continue; }
                for (int o = 0; o < around.Length; o += 16)
                {
                    var hex = string.Join(" ", around.Skip(o).Take(16).Select(b => b.ToString("x2")));
                    var u16 = string.Join(",", Enumerable.Range(0, Math.Min(8, (around.Length - o) / 2)).Select(i => BitConverter.ToUInt16(around, o + i * 2)));
                    Console.WriteLine($"{o - before,5}: {hex,-47}  u16[{u16}]");
                }
            }
            Console.WriteLine("writes=0");
            return 0;
        }
        finally
        {
            session.Close();
        }
    }
    /// <summary>Read-only: decode a candidate box-score block (24-byte player rows keyed by UNIQUESIGNATUREID).</summary>
    public static int Box(Dictionary<string, string> options)
    {
        long at = Convert.ToInt64(options["at"].Replace("0x", ""), 16);
        var session = new PlayerSession("NBA2K27.exe", options.GetValueOrDefault("data-dir"));
        try
        {
            var items = session.Connect();
            var model = session.Model;
            var bySig = new Dictionary<uint, string>();
            var byUid = new Dictionary<ushort, string>();
            var itemByUid = new Dictionary<ushort, RecordListItem>();
            foreach (var item in items)
            {
                var b = model.Memory.ReadBytes(item.Address, 300);
                bySig.TryAdd(BitConverter.ToUInt32(b, 88), item.Label);
                byUid.TryAdd(BitConverter.ToUInt16(b, 296), item.Label);
                itemByUid.TryAdd(BitConverter.ToUInt16(b, 296), item);
            }
            long date = long.Parse(options.GetValueOrDefault("date", "0"), CultureInfo.InvariantCulture);
            var hkeys = session.Specs.Keys.Where(k => k.StartsWith("Stats/History/GAMESCOREHISTORY", StringComparison.Ordinal)).ToArray();
            string Form(ushort uid)
            {
                if (date == 0 || !itemByUid.TryGetValue(uid, out var item)) return "";
                var h = session.ReadPlayer(item, hkeys);
                for (int slot = 0; slot < 5; slot++)
                {
                    long d = Convert.ToInt64(h.Values[$"Stats/History/GAMESCOREHISTORYDATE{slot}"].Raw ?? 0L, CultureInfo.InvariantCulture);
                    if (d != date) continue;
                    long m = Convert.ToInt64(h.Values[$"Stats/History/GAMESCOREHISTORYMINUTES{slot}"].Raw ?? 0L, CultureInfo.InvariantCulture);
                    float gs = BitConverter.Int32BitsToSingle((int)Convert.ToInt64(h.Values[$"Stats/History/GAMESCOREHISTORYVALUE{slot}"].Raw ?? 0L, CultureInfo.InvariantCulture));
                    return $"[min {m} gs {gs:0.0}]";
                }
                return "[-]";
            }
            int length = int.Parse(options.GetValueOrDefault("length", "1200"), CultureInfo.InvariantCulture);
            byte[] bytes = model.Memory.ReadBytes(at, length);
            for (int o = 0; o + 24 <= bytes.Length; o += 24)
            {
                uint id = BitConverter.ToUInt32(bytes, o);
                ushort lo = BitConverter.ToUInt16(bytes, o);
                string who = bySig.TryGetValue(id, out var s) ? $"sig:{s}" : byUid.TryGetValue(lo, out var u) ? $"uid:{u}" : "";
                if (id == 0) continue;
                Console.WriteLine($"{o,5}: {id:X8} {who,-24} {Form(lo),-18} b2={bytes[o + 2],3} b3={(sbyte)bytes[o + 3],4} | " + string.Join(" ", bytes.Skip(o + 4).Take(20).Select(x => $"{x,3}")));
            }
            Console.WriteLine("writes=0");
            return 0;
        }
        finally
        {
            session.Close();
        }
    }
    /// <summary>Read-only: retired jerseys through HistorySession (catalog + record read), as the editor uses them.</summary>
    public static int JerseyHistory(Dictionary<string, string> options)
    {
        var session = new PlayerSession("NBA2K27.exe", options.GetValueOrDefault("data-dir"));
        try
        {
            session.Connect();
            var history = new HistorySession(session);
            int total = 0;
            foreach (var team in session.Model.ScanRecords("Teams").Take(31))
            {
                var items = history.Catalog("jersey", team.Index);
                total += items.Count;
                if (team.Index is 4 or 0)
                {
                    Console.WriteLine($"{team.Label}: " + string.Join(" | ", items.Select(i => $"{i.Summary} {i.Label} {i.Year}")));
                    var read = history.Read(items[1].Key);
                    Console.WriteLine("  read " + read.Item.Key + ": " + string.Join(" ", read.Fields.Select(f => $"{f.Key}={read.Values[f.Key]}")));
                }
            }
            Console.WriteLine($"jerseys via catalog={total} writes=0");
            return 0;
        }
        finally
        {
            session.Close();
        }
    }
    /// <summary>Read-only: list game-record headers (date u32 repeated twice) inside the region containing an address.</summary>
    public static int Games(Dictionary<string, string> options)
    {
        long near = Convert.ToInt64(options["near"].Replace("0x", ""), 16);
        var session = new PlayerSession("NBA2K27.exe", options.GetValueOrDefault("data-dir"));
        try
        {
            session.Connect();
            var memory = (Nba2k27Editor.Core.Memory.Win32Memory)session.Memory;
            var region = memory.PrivateRegions().First(r => near >= r.Base && near < r.Base + r.Size);
            Console.WriteLine($"region 0x{region.Base:X} size {region.Size}");
            byte[] bytes = memory.ReadBytes(region.Base, (int)region.Size);
            long last = 0;
            int count = 0;
            for (int i = 0; i + 8 <= bytes.Length; i += 4)
            {
                uint a = BitConverter.ToUInt32(bytes, i);
                if (a < 1215000000 || a > 1250000000 || BitConverter.ToUInt32(bytes, i + 4) != a) continue;
                long at = region.Base + i;
                var date = DateTimeOffset.FromUnixTimeSeconds(a);
                string head = string.Join(" ", bytes.Skip(i + 8).Take(40).Select(x => x.ToString("x2")));
                Console.WriteLine($"0x{at:X} +{(last == 0 ? 0 : at - last),6} {date:yyyy-MM-dd HH:mm} | {head}");
                last = at;
                if (++count > int.Parse(options.GetValueOrDefault("limit", "80"), CultureInfo.InvariantCulture)) break;
            }
            Console.WriteLine($"headers={count} writes=0");
            return 0;
        }
        finally
        {
            session.Close();
        }
    }
    /// <summary>Read-only: find 8-byte values in [from, to] across private memory and the module image.</summary>
    public static int FindPointer(Dictionary<string, string> options)
    {
        long from = Convert.ToInt64(options["from"].Replace("0x", ""), 16);
        long to = Convert.ToInt64(options["to"].Replace("0x", ""), 16);
        var session = new PlayerSession("NBA2K27.exe", options.GetValueOrDefault("data-dir"));
        try
        {
            session.Connect();
            var memory = (Nba2k27Editor.Core.Memory.Win32Memory)session.Memory;
            int hits = 0;
            IReadOnlyList<(long Base, long Size)> regions = memory.PrivateRegions();
            if (options.ContainsKey("image-only"))
            {
                using var process = System.Diagnostics.Process.GetProcessById(memory.Pid);
                regions = new[] { (memory.BaseAddress, (long)process.MainModule!.ModuleMemorySize) };
            }
            foreach (var (baseAddress, size) in regions)
            {
                int step = options.ContainsKey("image-only") ? 4096 : 1 << 22;
                for (long at = 0; at < size; at += step)
                {
                    int chunk = (int)Math.Min(step, size - at);
                    byte[] bytes;
                    try { bytes = memory.ReadBytes(baseAddress + at, chunk); } catch (InvalidOperationException) { continue; }
                    for (int i = 0; i + 8 <= bytes.Length; i += 8)
                    {
                        long v = BitConverter.ToInt64(bytes, i);
                        if (v < from || v > to) continue;
                        long where = baseAddress + at + i;
                        string ctx = "";
                        try { ctx = string.Join(" ", memory.ReadBytes(where - 16, 48).Select(x => x.ToString("x2"))); } catch (InvalidOperationException) { }
                        Console.WriteLine($"0x{where:X} -> 0x{v:X} | {ctx}");
                        if (++hits > 40) { Console.WriteLine("writes=0"); return 0; }
                    }
                }
            }
            Console.WriteLine($"hits={hits} writes=0");
            return 0;
        }
        finally
        {
            session.Close();
        }
    }
    /// <summary>Read-only: decode one 1356-byte game record (address of its first date) with player lines.</summary>
    public static int GameRecord(Dictionary<string, string> options)
    {
        long at = Convert.ToInt64(options["at"].Replace("0x", ""), 16);
        var session = new PlayerSession("NBA2K27.exe", options.GetValueOrDefault("data-dir"));
        try
        {
            var items = session.Connect();
            var model = session.Model;
            var byUid = new Dictionary<ushort, (string Name, string Team)>();
            var teamOf = new Dictionary<long, string>();
            foreach (var club in model.ScanRecords("Teams", 512).Take(30))
            {
                var tb = model.Memory.ReadBytes(club.Address, 8 * 20);
                for (int s = 0; s < 20; s++) teamOf.TryAdd(BitConverter.ToInt64(tb, s * 8), $"#{club.Index} {club.Label}");
            }
            foreach (var item in items)
            {
                var b = model.Memory.ReadBytes(item.Address, 300);
                byUid.TryAdd(BitConverter.ToUInt16(b, 296), (item.Label, teamOf.GetValueOrDefault(item.Address, "")));
            }
            var teams = model.ScanRecords("Teams", 512).ToArray();
            byte[] r = model.Memory.ReadBytes(at - 64, 1356 + 128);
            int o0 = 64;
            Console.WriteLine("pre: " + Convert.ToHexString(r, 0, 64));
            Console.WriteLine($"date {DateTimeOffset.FromUnixTimeSeconds(BitConverter.ToUInt32(r, o0)):yyyy-MM-dd HH:mm:ss} id={BitConverter.ToUInt32(r, o0 + 8)} hdr u16: " + string.Join(",", Enumerable.Range(0, 4).Select(i => BitConverter.ToUInt16(r, o0 + 16 + i * 2))));
            int side = 0;
            var totals = new int[2];
            for (int o = o0 + 24; o + 24 <= o0 + 1356; o += 24)
            {
                ushort uid = BitConverter.ToUInt16(r, o);
                bool known = byUid.TryGetValue(uid, out var who);
                bool empty = r.Skip(o + 2).Take(22).All(x => x == 0);
                if (!known)
                {
                    Console.WriteLine($"  +{o - o0,4}: ---- {Convert.ToHexString(r, o, 24)}");
                    if (o > o0 + 24) side = 1;
                    continue;
                }
                if (empty) continue;
                ulong bitsLo = BitConverter.ToUInt64(r, o + 16);
                int ast = (int)(bitsLo & 15), ftm = (int)((bitsLo >> 7) & 15), oreb = (int)((bitsLo >> 14) & 7), tov = (int)((bitsLo >> 22) & 7), pf = (int)((bitsLo >> 40) & 7);
                int fgm = r[o + 4], fga = r[o + 5], tpm = r[o + 6] >> 1, tpa = r[o + 7], fta = r[o + 8], stl = r[o + 9], blk = r[o + 10], dreb = r[o + 11];
                int pts = 2 * fgm + tpm + ftm;
                totals[side] += pts;
                Console.WriteLine($"  +{o - o0,4}: {who.Name,-22} {who.Team,-22} min {r[o + 2] / 2.0,4} pm {(sbyte)r[o + 3],3} pts {pts,2} {fgm}/{fga} 3p {tpm}/{tpa} ft {ftm}/{fta} reb {oreb}+{dreb} ast {ast} stl {stl} blk {blk} tov {tov} pf {pf} | rest {Convert.ToHexString(r, o + 12, 4)} {Convert.ToHexString(r, o + 22, 2)}");
            }
            Console.WriteLine($"points by side: {totals[0]} / {totals[1]}");
            Console.WriteLine("tail: " + Convert.ToHexString(r, o0 + 1356 - 32, 32 + 64));
            Console.WriteLine("writes=0");
            return 0;
        }
        finally
        {
            session.Close();
        }
    }
    /// <summary>Read-only: find places where two team record pointers sit 8 bytes apart (schedule-like rows).</summary>
    public static int TeamPairs(Dictionary<string, string> options)
    {
        int a = int.Parse(options["a"], CultureInfo.InvariantCulture), b = int.Parse(options["b"], CultureInfo.InvariantCulture);
        var session = new PlayerSession("NBA2K27.exe", options.GetValueOrDefault("data-dir"));
        try
        {
            session.Connect();
            var memory = (Nba2k27Editor.Core.Memory.Win32Memory)session.Memory;
            var teams = session.Model.ScanRecords("Teams", 512).ToArray();
            long pa = teams.Single(t => t.Index == a).Address, pb = teams.Single(t => t.Index == b).Address;
            int hits = 0;
            foreach (var (baseAddress, size) in memory.PrivateRegions())
            {
                for (long at = 0; at < size; at += 1 << 22)
                {
                    int chunk = (int)Math.Min((1 << 22) + 16, size - at);
                    byte[] bytes;
                    try { bytes = memory.ReadBytes(baseAddress + at, chunk); } catch (InvalidOperationException) { continue; }
                    for (int i = 8; i + 16 <= bytes.Length && i < (1 << 22); i += 8)
                    {
                        long v = BitConverter.ToInt64(bytes, i), w = BitConverter.ToInt64(bytes, i + 8);
                        if (!(v == pa && w == pb || v == pb && w == pa)) continue;
                        long where = baseAddress + at + i;
                        uint code = BitConverter.ToUInt32(bytes, i - 8);
                        int hi = (int)(code >> 16);
                        string date = $"{1972 + (hi >> 9)}-{((hi >> 5) & 15) + 1:00}-{hi & 31:00}";
                        Console.WriteLine($"0x{where:X} {(v == pa ? "a,b" : "b,a")} code=0x{code:X8} {date} | {Convert.ToHexString(bytes, i - 8, 32)}");
                        if (++hits > 60) { Console.WriteLine("writes=0"); return 0; }
                    }
                }
            }
            Console.WriteLine($"hits={hits} writes=0");
            return 0;
        }
        finally
        {
            session.Close();
        }
    }
    /// <summary>Read-only: every game record in the region (1356-byte records): id, header, teams by roster, points.</summary>
    public static int GameList(Dictionary<string, string> options)
    {
        long near = Convert.ToInt64(options["near"].Replace("0x", ""), 16);
        var session = new PlayerSession("NBA2K27.exe", options.GetValueOrDefault("data-dir"));
        try
        {
            var items = session.Connect();
            var model = session.Model;
            var memory = (Nba2k27Editor.Core.Memory.Win32Memory)session.Memory;
            var teamOf = new Dictionary<long, int>();
            var clubs = model.ScanRecords("Teams", 512).Take(30).ToArray();
            foreach (var club in clubs)
            {
                var tb = model.Memory.ReadBytes(club.Address, 8 * 20);
                for (int s = 0; s < 20; s++) teamOf.TryAdd(BitConverter.ToInt64(tb, s * 8), club.Index);
            }
            var uidTeam = new Dictionary<ushort, int>();
            foreach (var item in items)
            {
                ushort uid = BitConverter.ToUInt16(model.Memory.ReadBytes(item.Address + 296, 2));
                if (teamOf.TryGetValue(item.Address, out int club)) uidTeam.TryAdd(uid, club);
            }
            var region = memory.PrivateRegions().First(r => near >= r.Base && near < r.Base + r.Size);
            byte[] bytes = memory.ReadBytes(region.Base, (int)region.Size);
            int first = -1;
            for (int i = 0; i + 8 <= bytes.Length; i += 4)
            {
                uint a = BitConverter.ToUInt32(bytes, i);
                if (a >= 1215000000 && a <= 1250000000 && BitConverter.ToUInt32(bytes, i + 4) == a) { first = i; break; }
            }
            var label = clubs.ToDictionary(c => c.Index, c => c.Label);
            foreach (var club in clubs) Console.WriteLine($"team #{club.Index} {club.Label} uid={BitConverter.ToUInt16(model.Memory.ReadBytes(club.Address + 3872, 2))} base={BitConverter.ToUInt16(model.Memory.ReadBytes(club.Address + 3878, 2))}");
            int n = 0;
            for (int o = first; o + 1356 <= bytes.Length; o += 1356)
            {
                uint date = BitConverter.ToUInt32(bytes, o);
                if (date < 1215000000 || date > 1250000000) break;
                int id = BitConverter.ToInt32(bytes, o + 8);
                var sideTeams = new int[2] { -1, -1 };
                var pts = new int[2];
                for (int side = 0; side < 2; side++)
                {
                    for (int p = 0; p < 15; p++)
                    {
                        int r = o + 24 + side * 600 + p * 24;
                        ushort uid = BitConverter.ToUInt16(bytes, r);
                        if (uid == 0 || bytes[r + 2] == 0) continue;
                        if (sideTeams[side] < 0 && uidTeam.TryGetValue(uid, out int club)) sideTeams[side] = club;
                        ulong lo = BitConverter.ToUInt64(bytes, r + 16);
                        pts[side] += 2 * bytes[r + 4] + (bytes[r + 6] >> 1) + (int)((lo >> 7) & 15);
                    }
                }
                string Name(int x) => x >= 0 && label.TryGetValue(x, out var l) ? l : "?";
                Console.WriteLine($"{n,4} id={id,5} raw={date:X8} t={DateTimeOffset.FromUnixTimeSeconds(date):MM-dd HH:mm} hdr={BitConverter.ToUInt16(bytes, o + 16),3},{BitConverter.ToUInt16(bytes, o + 18),3} x={BitConverter.ToUInt16(bytes, o + 20),5},{BitConverter.ToUInt16(bytes, o + 22),5} k={BitConverter.ToUInt32(bytes, o + 12):X8} {Name(sideTeams[0])} {pts[0]} - {pts[1]} {Name(sideTeams[1])}");
                n++;
            }
            Console.WriteLine($"games={n} writes=0");
            return 0;
        }
        finally
        {
            session.Close();
        }
    }
    /// <summary>Read-only: breadth-first pointer walk from a base entry, reporting paths that reach [from, to].</summary>
    public static int Chain(Dictionary<string, string> options)
    {
        long from = Convert.ToInt64(options["from"].Replace("0x", ""), 16);
        long to = Convert.ToInt64(options["to"].Replace("0x", ""), 16);
        int depth = int.Parse(options.GetValueOrDefault("depth", "3"), CultureInfo.InvariantCulture);
        int span = int.Parse(options.GetValueOrDefault("span", "4096"), CultureInfo.InvariantCulture);
        var session = new PlayerSession("NBA2K27.exe", options.GetValueOrDefault("data-dir"));
        try
        {
            session.Connect();
            var model = session.Model;
            var memory = (Nba2k27Editor.Core.Memory.Win32Memory)session.Memory;
            var regions = memory.PrivateRegions();
            bool Heap(long v) => v > 0x10000 && (v & 7) == 0 && regions.Any(r => v >= r.Base && v < r.Base + r.Size);
            long root = options.TryGetValue("root", out var rootText) ? Convert.ToInt64(rootText.Replace("0x", ""), 16) : model.ResolveBasePointerEntry(model.BasePointerEntry("RosterData"), stopAtNull: true);
            Console.WriteLine($"root 0x{root:X}");
            var queue = new Queue<(long Address, string Path, int Depth)>();
            var seen = new HashSet<long> { root };
            queue.Enqueue((root, "root", 0));
            int found = 0, visited = 0;
            while (queue.Count > 0 && visited < 20000)
            {
                var (address, path, d) = queue.Dequeue();
                visited++;
                byte[] bytes;
                try { bytes = memory.ReadBytes(address, span); } catch (InvalidOperationException) { continue; }
                for (int o = 0; o + 8 <= bytes.Length; o += 8)
                {
                    long v = BitConverter.ToInt64(bytes, o);
                    if (v >= from && v <= to)
                    {
                        Console.WriteLine($"{path}+{o} -> 0x{v:X}");
                        if (++found > 20) { Console.WriteLine("writes=0"); return 0; }
                    }
                    if (d + 1 <= depth && Heap(v) && seen.Add(v)) queue.Enqueue((v, $"{path}+{o}", d + 1));
                }
            }
            Console.WriteLine($"visited={visited} found={found} writes=0");
            return 0;
        }
        finally
        {
            session.Close();
        }
    }
    /// <summary>Read-only: decode the whole game table with SeasonGames and check internal consistency.</summary>
    public static int GamesCheck(Dictionary<string, string> options)
    {
        var session = new PlayerSession("NBA2K27.exe", options.GetValueOrDefault("data-dir"));
        try
        {
            session.Connect();
            var memory = (Nba2k27Editor.Core.Memory.Win32Memory)session.Memory;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var table = SeasonGames.Locate(memory) ?? throw new InvalidOperationException("table not found");
            Console.WriteLine($"table 0x{table.Address:X} count={table.Count} locate {watch.ElapsedMilliseconds}ms");
            var games = SeasonGames.Read(memory, table.Address, table.Count);
            int played = 0, quarterMismatch = 0, shotErrors = 0, overtime = 0;
            int maxAst = 0, maxTov = 0, maxOreb = 0, maxFtm = 0, maxPf = 0;
            foreach (var g in games.Where(g => g.Played))
            {
                played++;
                int hq = g.HomeQuarters.Sum(), aq = g.AwayQuarters.Sum();
                if (hq != g.HomePoints || aq != g.AwayPoints)
                {
                    if (g.HomePoints > hq && g.AwayPoints > aq) { overtime++; if (overtime <= 3) { var raw = memory.ReadBytes(g.Address + 1272, 84); Console.WriteLine($"  ot id={g.Id} pts {g.HomePoints}/{g.AwayPoints} q {hq}/{aq} tail {Convert.ToHexString(raw)}"); } }
                    else { quarterMismatch++; if (quarterMismatch <= 3) Console.WriteLine($"  mismatch id={g.Id} {g.Date} q {hq}/{aq} pts {g.HomePoints}/{g.AwayPoints}"); }
                }
                foreach (var l in g.Home.Concat(g.Away))
                {
                    if (l.FtMade > l.FtAttempted || l.FgMade > l.FgAttempted || l.ThreeMade > l.ThreeAttempted || l.ThreeMade > l.FgMade || l.Fouls > 6) shotErrors++;
                    maxAst = Math.Max(maxAst, l.Assists); maxTov = Math.Max(maxTov, l.Turnovers); maxOreb = Math.Max(maxOreb, l.OffensiveRebounds); maxFtm = Math.Max(maxFtm, l.FtMade); maxPf = Math.Max(maxPf, l.Fouls);
                }
            }
            Console.WriteLine($"teams in table: {games.Select(g => g.HomeUid).Concat(games.Select(g => g.AwayUid)).Distinct().Count()} played={played} scheduled={games.Count - played} quarterMismatch={quarterMismatch} overtimeLike={overtime} lineErrors={shotErrors}");
            Console.WriteLine($"max ast={maxAst} tov={maxTov} oreb={maxOreb} ftm={maxFtm} pf={maxPf}");
            var first = games.First(); var last = games.Last();
            Console.WriteLine($"dates {games.Min(g => g.Date)} .. {games.Max(g => g.Date)}; last played {games.Where(g => g.Played).Max(g => g.Date)}");
            foreach (var g in games.Where(g => g.HomeUid == 23 || g.AwayUid == 23).Where(g => g.Date >= new DateOnly(2008, 12, 19) && g.Date <= new DateOnly(2009, 1, 4)))
                Console.WriteLine($"  rockets {g.Date} home={g.HomeUid} away={g.AwayUid} played={g.Played} {g.HomePoints}-{g.AwayPoints} q={string.Join(",", g.HomeQuarters)}|{string.Join(",", g.AwayQuarters)}");
            var iv = games.Where(g => g.Played).SelectMany(g => g.Home.Concat(g.Away).Select(l => (g, l))).Where(x => x.l.PlayerUid == 0x51C7).OrderByDescending(x => x.g.Date).Take(3);
            foreach (var (g, l) in iv) Console.WriteLine($"  iverson {g.Date} {l.Minutes}min {l.Points}pts {l.FgMade}/{l.FgAttempted} 3p {l.ThreeMade}/{l.ThreeAttempted} ft {l.FtMade}/{l.FtAttempted} reb {l.OffensiveRebounds}+{l.DefensiveRebounds} ast {l.Assists} stl {l.Steals} blk {l.Blocks} tov {l.Turnovers} pf {l.Fouls} pm {l.PlusMinus}");
            Console.WriteLine($"total {watch.ElapsedMilliseconds}ms writes=0");
            return 0;
        }
        finally
        {
            session.Close();
        }
    }
}
