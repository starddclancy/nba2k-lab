using System.Text.Json.Nodes;
using Nba2k27Editor.Core.Editing;
using Nba2k27Editor.Core.Gs;
using Nba2k27Editor.Core.Match;
using Nba2k27Editor.Core.Model;
using Nba2k27Editor.Core.Roster;

namespace Nba2k27Editor.WebHost;

/// <summary>
/// Command dispatcher behind the WebView2 message bridge. Every reply is
/// <c>{ id, ok, data | error }</c> so the page can match responses to requests.
/// </summary>
internal sealed partial class Bridge : IDisposable
{
    private readonly bool _matchDebugReads;
    private readonly bool _matchDiagnosticWrites;
    private readonly bool _scheduleWrites;

    public Bridge(bool matchDebugReads = false, bool matchDiagnosticWrites = false, bool scheduleWrites = false)
    {
        _matchDebugReads = matchDebugReads;
        _matchDiagnosticWrites = matchDiagnosticWrites;
        _scheduleWrites = scheduleWrites;
    }

    private readonly object _gate = new();
    private readonly PlayerReadCache _playerReads = new();
    private PlayerSession? _session;
    private HistorySession? _history;
    private Dictionary<int, RecordListItem> _byIndex = new();
    private Dictionary<int, RecordListItem> _draftByIndex = new();
    private Dictionary<int, ProspectInfo> _prospectByIndex = new();
    private string _prospectStatus = "未读取名单内待选秀球员";
    private Dictionary<long, string> _teamByAddress = new();
    private NativeGeneralGsSession? _gs;
    private GeneralGsSnapshot? _gsSnapshot;
    private NativeMenuGsSession? _inMatch;
    private LegacyGsSnapshot? _inMatchSnapshot;
    internal int PlayerWriteAttempts => _session?.Memory is Nba2k27Editor.Core.Memory.Win32Memory memory
        ? memory.Writes : 0;
    internal int TotalWriteAttempts => PlayerWriteAttempts + (_gs?.WriteAttempts ?? 0)
        + (_inMatch?.WriteAttempts ?? 0) + (_matchConnection?.WriteAttempts ?? 0);

    /// <summary>
    /// Set by the window so file picking can show a dialog on the UI thread; the bridge itself
    /// runs off the UI thread and must not open dialogs directly. The argument is a filter string.
    /// </summary>
    public Func<string, string?>? PickFile { get; set; }

    public string Handle(string raw)
    {
        string id = string.Empty;
        try
        {
            JsonObject root = JsonNode.Parse(raw)?.AsObject()
                ?? throw new InvalidOperationException("请求为空");
            id = root["id"]?.GetValue<string>() ?? string.Empty;
            string command = root["command"]?.GetValue<string>()
                ?? throw new InvalidOperationException("缺少命令");
            JsonObject payload = root["payload"] as JsonObject ?? new JsonObject();
            JsonNode? data = Dispatch(command, payload);
            return new JsonObject { ["id"] = id, ["ok"] = true, ["data"] = data }
                .ToJsonString(Program.JsonOptions);
        }
        catch (Exception exc)
        {
            var reply = new JsonObject { ["id"] = id, ["ok"] = false, ["error"] = exc.Message };
            if (exc is MatchSampleChangedException)
            {
                // Live refresh skips this sample and keeps the previous one on screen.
                reply["code"] = "matchChanged";
            }
            return reply.ToJsonString(Program.JsonOptions);
        }
    }

    private JsonNode? Dispatch(string command, JsonObject payload)
    {
        switch (command)
        {
            case "ping":
                return new JsonObject { ["pong"] = true, ["assets"] = EmbeddedAssets.Paths.Count };
            case "matchStatus":
                return MatchStatus();
            case "matchRead":
                lock (_gate)
                {
                    return MatchRead();
                }
            case "matchPrepare":
                lock (_gate)
                {
                    return MatchPrepare(payload);
                }
            case "matchApply":
                lock (_gate)
                {
                    return MatchApply(payload);
                }
            case "matchUndo":
                lock (_gate)
                {
                    return MatchUndo();
                }
            case "teamSeasons":
            case "historyList":
            case "historyRead":
            case "historyApply":
            case "historyUndo":
                lock (_gate)
                {
                    return HistoryCommand(command, payload);
                }
            case "connect":
                lock (_gate)
                {
                    return Connect();
                }
            case "listPlayers":
                lock (_gate)
                {
                    return ListPlayers(
                        payload["query"]?.GetValue<string>() ?? string.Empty,
                        payload["team"]?.GetValue<string>(),
                        payload["scope"]?.GetValue<string>() ?? "league");
                }
            case "rookies":
                lock (_gate)
                {
                    return Rookies();
                }
            case "prospects":
                lock (_gate)
                {
                    return Prospects(payload["query"]?.GetValue<string>() ?? string.Empty);
                }
            case "readPlayer":
                lock (_gate)
                {
                    return ReadPlayer(
                        Required(payload, "index").GetValue<int>(),
                        payload["source"]?.GetValue<string>() ?? "roster");
                }
            case "seasonGames":
                lock (_gate)
                {
                    return SeasonGamesRead(payload["diagnostics"]?.GetValue<bool>() == true);
                }
            case "schedulePrepare":
                lock (_gate) { return SchedulePrepare(payload); }
            case "scheduleApply":
                lock (_gate) { return ScheduleApply(payload); }
            case "scheduleUndo":
                lock (_gate) { return ScheduleUndo(); }
            case "leagueSeason":
                lock (_gate)
                {
                    return LeagueSeason();
                }
            case "leagueTeamSeason":
                lock (_gate)
                {
                    return LeagueTeamSeason();
                }
            case "playerForm":
                lock (_gate)
                {
                    return PlayerForm(Required(payload, "index").GetValue<int>(), payload["source"]?.GetValue<string>() ?? "roster");
                }
            case "teamForm":
                lock (_gate)
                {
                    return TeamFormRead(payload["teamIndex"]?.GetValue<int>());
                }            case "readPlayerStats":
                lock (_gate)
                {
                    return ReadPlayerStats(Required(payload, "index").GetValue<int>(),
                        payload["source"]?.GetValue<string>() ?? "roster");
                }
            case "applyPlayer":
                lock (_gate)
                {
                    return ApplyPlayer(
                        Required(payload, "index").GetValue<int>(),
                        Required(payload, "changes").AsObject(),
                        payload["source"]?.GetValue<string>() ?? "roster",
                        ReadToken(payload));
                }
            case "undoPlayer":
                lock (_gate)
                {
                    return UndoPlayer();
                }
            case "readBatch":
                lock (_gate)
                {
                    return ReadBatch(
                        Required(payload, "indices").AsArray(),
                        Required(payload, "field").GetValue<string>(),
                        payload["source"]?.GetValue<string>() ?? "roster");
                }
            case "applyBatch":
                lock (_gate)
                {
                    return ApplyBatch(
                        Required(payload, "indices").AsArray(),
                        Required(payload, "changes").AsObject(),
                        payload["source"]?.GetValue<string>() ?? "roster",
                        ReadToken(payload));
                }
            case "gsRead":
                lock (_gate)
                {
                    return GsRead();
                }
            case "gsApply":
                lock (_gate)
                {
                    return GsApply(Required(payload, "changes").AsObject());
                }
            case "gsUndo":
                lock (_gate)
                {
                    return GsUndo();
                }
            case "inMatchRead":
                lock (_gate)
                {
                    return InMatchRead();
                }
            case "inMatchApply":
                lock (_gate)
                {
                    return InMatchApply(Required(payload, "changes").AsObject());
                }
            case "inMatchUndo":
                lock (_gate)
                {
                    return InMatchUndo();
                }
            case "teams":
                lock (_gate)
                {
                    return Teams();
                }
            case "placement":
                lock (_gate)
                {
                    return Placement(Required(payload, "index").GetValue<int>());
                }
            case "planMove":
                lock (_gate)
                {
                    return PlanMove(
                        Required(payload, "index").GetValue<int>(),
                        Required(payload, "team").GetValue<int>(),
                        SyncPayroll(payload));
                }
            case "applyMove":
                lock (_gate)
                {
                    return ApplyMove(
                        Required(payload, "index").GetValue<int>(),
                        Required(payload, "team").GetValue<int>(),
                        SyncPayroll(payload));
                }
            case "planTrade":
                lock (_gate)
                {
                    return PlanTrade(
                        Required(payload, "first").GetValue<int>(),
                        Required(payload, "second").GetValue<int>(),
                        PickList(payload, "firstPicks"), PickList(payload, "secondPicks"), SyncPayroll(payload));
                }
            case "applyTrade":
                lock (_gate)
                {
                    return ApplyTrade(
                        Required(payload, "first").GetValue<int>(),
                        Required(payload, "second").GetValue<int>(),
                        PickList(payload, "firstPicks"), PickList(payload, "secondPicks"), SyncPayroll(payload));
                }
            case "planPackage":
            case "applyPackage":
                lock (_gate)
                {
                    return TradePackage(payload, apply: command == "applyPackage");
                }
            case "playerRatings":
                lock (_gate)
                {
                    return PlayerRatings();
                }
            case "draftPicks":
                lock (_gate)
                {
                    return DraftPicks();
                }
            case "undoMove":
                lock (_gate)
                {
                    return UndoMove();
                }
            case "presetList":
                lock (_gate)
                {
                    return PresetList();
                }
            case "presetLoad":
                lock (_gate)
                {
                    return PresetLoad(Required(payload, "name").GetValue<string>());
                }
            case "presetSave":
                lock (_gate)
                {
                    return PresetSave(
                        Required(payload, "name").GetValue<string>(),
                        Required(payload, "values").AsObject());
                }
            case "presetDelete":
                lock (_gate)
                {
                    return PresetDelete(Required(payload, "name").GetValue<string>());
                }
            case "exportPlayer":
                lock (_gate)
                {
                    return ExportPlayer(Required(payload, "index").GetValue<int>(),
                        payload["source"]?.GetValue<string>() ?? "roster");
                }
            case "importPlayer":
                lock (_gate)
                {
                    return ImportPlayer(Required(payload, "index").GetValue<int>(),
                        payload["source"]?.GetValue<string>() ?? "roster",
                        ReadToken(payload), payload["preview"]?.GetValue<bool>() ?? false);
                }
            case "libraryList":
                lock (_gate)
                {
                    return LibraryList();
                }
            case "librarySave":
                lock (_gate)
                {
                    return LibrarySave(Required(payload, "index").GetValue<int>(),
                        Required(payload, "kind").GetValue<string>(),
                        Required(payload, "name").GetValue<string>(),
                        payload["notes"]?.GetValue<string>() ?? string.Empty);
                }
            case "libraryApply":
                lock (_gate)
                {
                    return LibraryApply(Required(payload, "id").GetValue<string>(),
                        Required(payload, "index").GetValue<int>(),
                        Required(payload, "scope").GetValue<string>(),
                        ReadToken(payload), payload["preview"]?.GetValue<bool>() ?? false);
                }
            case "libraryDelete":
                lock (_gate)
                {
                    return LibraryDelete(Required(payload, "id").GetValue<string>());
                }
            case "libraryImport":
                lock (_gate)
                {
                    return LibraryImport();
                }
            case "journalList":
                lock (_gate)
                {
                    return JournalList();
                }
            case "avatarGet":
                lock (_gate)
                {
                    return AvatarGet(Required(payload, "key").GetValue<string>());
                }
            case "avatarPick":
                lock (_gate)
                {
                    return AvatarPick(Required(payload, "key").GetValue<string>());
                }
            case "avatarClear":
                lock (_gate)
                {
                    return AvatarClear(Required(payload, "key").GetValue<string>());
                }
            case "listTeams":
                lock (_gate)
                {
                    return ListTeams(payload["scope"]?.GetValue<string>() ?? "league");
                }
            case "readTeam":
                lock (_gate)
                {
                    return ReadTeam(Required(payload, "index").GetValue<int>());
                }
            case "applyTeam":
                lock (_gate)
                {
                    return ApplyTeam(
                        Required(payload, "index").GetValue<int>(),
                        Required(payload, "changes").AsObject());
                }
            case "undoTeam":
                lock (_gate)
                {
                    return UndoTeam();
                }
            default:
                throw new InvalidOperationException($"未知命令：{command}");
        }
    }

    private static JsonNode Required(JsonObject payload, string name) =>
        payload[name] ?? throw new InvalidOperationException($"缺少参数：{name}");

    private static string? ReadToken(JsonObject payload) =>
        payload.TryGetPropertyValue("readToken", out JsonNode? token)
            ? token?.GetValue<string>() ?? throw new InvalidOperationException("读取快照不能为空，请重新读取")
            : null;

    private JsonNode Connect()
    {
        _playerReads.Clear();
        _session?.Close();
        _session = new PlayerSession();
        _history = new HistorySession(_session);
        _schedule = null;
        _gameTable = null;
        _historyUndoKeys.Clear();
        _mover = null;
        _teamOptions = null;
        _teamItems = null;
        _teamTable = 0;
        _teamUndo.Clear();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        _session.Model.RecoverBasePointers();
        long discoverMs = watch.ElapsedMilliseconds;
        IReadOnlyList<RecordListItem> items = _session.Connect(buildRosterIndex: false);
        long tableMs = watch.ElapsedMilliseconds - discoverMs;
        long indexStart = watch.ElapsedMilliseconds;
        _session.RefreshRosterIndex();
        long indexMs = watch.ElapsedMilliseconds - indexStart;
        Diagnostics.Note(
            $"connect: discover={discoverMs}ms table={tableMs}ms index={indexMs}ms players={items.Count}");
        _byIndex = new Dictionary<int, RecordListItem>();
        foreach (RecordListItem item in items)
        {
            _byIndex[item.Index] = item;
        }
        _draftByIndex = new Dictionary<int, RecordListItem>();
        foreach (RecordListItem item in _session.RosterIndex.DraftItems)
        {
            _draftByIndex[item.Index] = item;
        }
        _prospectByIndex.Clear();
        _prospectStatus = "未读取名单内待选秀球员";
        _teamByAddress = new Dictionary<long, string>();
        var leagueTeams = LeagueTeams();
        var leagueIndices = leagueTeams.Select(team => team.Index).ToHashSet();
        foreach (var team in _session.RosterIndex.Teams.Where(team => leagueIndices.Contains(team.Index)))
        {
            foreach (RecordListItem player in team.Players)
            {
                _teamByAddress.TryAdd(player.Address, team.Label);
            }
        }
        SessionEpoch epoch = _session.Epoch!;
        var memberships = LeagueMembership();
        var ageContext = PlayerAgeContext.Read(_session.Memory);
        return new JsonObject
        {
            ["pid"] = epoch.Pid,
            ["base"] = "0x" + epoch.Base.ToString("x", System.Globalization.CultureInfo.InvariantCulture),
            ["roster"] = memberships.Values.Count(LeagueRoster.IsCurrent),
            ["teams"] = leagueTeams.Count,
            ["resourcePlayers"] = items.Count,
            ["resourceTeams"] = TeamItems().Count,
            ["seasonEndYear"] = ageContext.SeasonEndYear,
            ["seasonError"] = ageContext.Error,
            ["draft"] = _session.RosterIndex.DraftItems.Count,
            ["draftStatus"] = _session.RosterIndex.DraftStatus,
            ["prospects"] = _prospectByIndex.Count,
            ["prospectStatus"] = _prospectStatus,
            ["fields"] = _session.Specs.Count,
            ["baseStatus"] = _session.Model.LastStatus,
            ["rootCache"] = _session.Model.RootCache?.FilePath ?? string.Empty,
            ["cacheWarning"] = _session.Model.RootCache?.LastWarning ?? string.Empty,
            ["memoryPid"] = _session.Memory.Pid,
            ["memoryBase"] = "0x" + _session.Memory.BaseAddress.ToString("x", System.Globalization.CultureInfo.InvariantCulture),
        };
    }

    private JsonNode ListPlayers(string query, string? team, string scope)
    {
        PlayerSession session = Session();
        session.Guard();
        session.RefreshRosterIndex();
        if (scope is not ("league" or "all")) throw new ArgumentException("未知名单范围");
        var leagueIndices = LeagueTeams().Select(item => item.Index).ToHashSet();
        var buckets = scope == "all" ? session.RosterIndex.Teams
            : session.RosterIndex.Teams.Where(bucket => leagueIndices.Contains(bucket.Index)).ToArray();
        _teamByAddress.Clear();
        foreach (var bucket in buckets)
        {
            foreach (var player in bucket.Players)
            {
                _teamByAddress.TryAdd(player.Address, bucket.Label);
            }
        }
        var memberships = PlayerMembership.Read(session.Model, session.Items, buckets);
        PlayerAgeContext ageContext = PlayerAgeContext.Read(session.Memory);
        var rows = new JsonArray();
        foreach (RecordListItem item in session.Items)
        {
            if (scope == "league" && !LeagueRoster.IsCurrent(memberships[item.Index])) continue;
            if (query.Length > 0
                && !item.Label.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            string label = _teamByAddress.TryGetValue(item.Address, out string? teamLabel)
                ? teamLabel
                : string.Empty;
            if (!string.IsNullOrEmpty(team) && !string.Equals(label, team, StringComparison.Ordinal))
            {
                continue;
            }
            rows.Add(new JsonObject
            {
                ["key"] = item.Index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["index"] = item.Index,
                ["name"] = item.Label,
                ["team"] = label,
                ["photoId"] = ReadPhotoId(session, item),
                ["summary"] = DescribeSummary(PlayerSummary.Read(session, item, ageContext)),
                ["category"] = memberships[item.Index].Category,
                ["teamIndices"] = new JsonArray(memberships[item.Index].TeamIndices
                    .Select(value => (JsonNode?)JsonValue.Create(value)).ToArray()),
                ["headScan"] = DescribeHeadScan(PlayerAppearance.ReadHeadScan(session.Model, item)),
            });
        }
        session.Guard();
        return rows;
    }

    private RecordListItem[] PlayerList(JsonObject payload, string key)
    {
        if (payload[key] is not JsonArray items)
        {
            return Array.Empty<RecordListItem>();
        }
        if (items.Count > 15)
        {
            throw new InvalidOperationException("一方最多送出 15 名球员");
        }
        return items.Select(item => Item(item?.GetValue<int>() ?? throw new InvalidOperationException("球员编号无效"))).ToArray();
    }

    /// <summary>Team-to-team trade package; planning never writes.</summary>
    private JsonNode TradePackage(JsonObject payload, bool apply)
    {
        RosterMover mover = Mover();
        MovePlan Plan() => mover.PlanPackage(
            Team(Required(payload, "firstTeam").GetValue<int>()), Team(Required(payload, "secondTeam").GetValue<int>()),
            PlayerList(payload, "firstPlayers"), PlayerList(payload, "secondPlayers"),
            PickList(payload, "firstPicks"), PickList(payload, "secondPicks"), SyncPayroll(payload));
        MovePlan plan = Plan();
        if (!apply)
        {
            return DescribePlan(plan, applied: false, patches: 0, journal: string.Empty);
        }
        RosterMoveTransaction transaction = mover.Apply(plan);
        _teamOptions = null;
        return DescribePlan(plan, applied: true, transaction.Patches.Count, transaction.JournalPath);
    }

    /// <summary>
    /// Read-only bulk columns for the player data table: one row per roster player, values in the
    /// order of "fields". Rating fields return display values; salary fields return raw dollars.
    /// </summary>
    private static readonly string[] RatingColumns =
    {
        "Attributes/Offense/CLOSESHOT", "Attributes/Offense/DRIVINGLAYUP", "Attributes/Offense/DRIVINGDUNK",
        "Attributes/Offense/STANDINGDUNK", "Attributes/Offense/POSTCONTROL", "Attributes/Offense/MIDRANGE",
        "Attributes/Offense/3POINT", "Attributes/Offense/FREETHROW", "Attributes/Offense/PASSACCURACY",
        "Attributes/Offense/BALLCONTROL", "Attributes/Athleticism/SPEEDWITHBALL", "Attributes/Defense/INTERIORDEFENSE",
        "Attributes/Defense/PERIMETERDEFENSE", "Attributes/Defense/STEAL", "Attributes/Defense/BLOCK",
        "Attributes/Offense/OFFENSIVEREBOUND", "Attributes/Defense/DEFENSEREBOUND", "Attributes/Athleticism/SPEED",
        "Attributes/Athleticism/AGILITY", "Attributes/Athleticism/STRENGTH", "Attributes/Athleticism/VERTICAL",
        "Attributes/Misc/POTENTIAL", "Contract/Salary/SALARYYEAR1", "Contract/Contract Terms/YEARSLEFT",
    };

    private JsonNode PlayerRatings()
    {
        PlayerSession session = Session();
        session.Guard();
        var specs = RatingColumns.Select(key => session.Specs.TryGetValue(key, out FieldSpec? spec) ? spec : null).ToArray();
        var rows = new JsonArray();
        foreach (RecordListItem item in LeaguePlayers())
        {
            var values = new JsonArray();
            for (int column = 0; column < RatingColumns.Length; column++)
            {
                FieldSpec? spec = specs[column];
                if (spec is null)
                {
                    values.Add(null);
                    continue;
                }
                try
                {
                    FieldRead read = session.Model.ReadField(spec.Entry, item);
                    object? value = RatingColumns[column].StartsWith("Attributes/", StringComparison.Ordinal)
                        ? read.DisplayValue : read.RawValue;
                    values.Add(value is null ? null : JsonValue.Create(Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture)));
                }
                catch (Exception exc) when (exc is InvalidOperationException or ArgumentException or FormatException or InvalidCastException)
                {
                    values.Add(null);
                }
            }
            rows.Add(new JsonObject { ["index"] = item.Index, ["values"] = values });
        }
        session.Guard();
        return new JsonObject
        {
            ["fields"] = new JsonArray(RatingColumns.Select(key => (JsonNode?)JsonValue.Create(key)).ToArray()),
            ["rows"] = rows,
        };
    }

    private void RefreshProspects()
    {
        PlayerSession session = Session();
        _prospectByIndex.Clear();
        session.Guard();
        ProspectCatalog catalog = ProspectBrowser.Read(session.Model, session.Items);
        session.Guard();
        _prospectByIndex = catalog.Records.Where(row => row.IsProspect)
            .ToDictionary(row => row.Item.Index);
        _prospectStatus = $"名单内待选秀 · {_prospectByIndex.Count} 人"
            + (catalog.Errors.Count > 0 ? $"，读取失败 {catalog.Errors.Count} 项" : string.Empty);
    }

    private JsonNode Prospects(string query)
    {
        PlayerSession session = Session();
        RefreshProspects();
        PlayerAgeContext ageContext = PlayerAgeContext.Read(session.Memory);
        var rows = new JsonArray();
        foreach (ProspectInfo prospect in _prospectByIndex.Values.OrderBy(row => row.ProspectIndex))
        {
            if (query.Length > 0 && !prospect.Item.Label.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            rows.Add(new JsonObject
            {
                ["key"] = prospect.Item.Index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["index"] = prospect.Item.Index,
                ["name"] = prospect.Item.Label,
                ["team"] = _teamByAddress.GetValueOrDefault(prospect.Item.Address, string.Empty),
                ["photoId"] = ReadPhotoId(session, prospect.Item),
                ["prospectIndex"] = prospect.ProspectIndex,
                ["draftYear"] = prospect.DraftYear,
                ["draftRound"] = prospect.Round,
                ["draftPick"] = prospect.Pick,
                ["birthYear"] = prospect.BirthYear,
                ["summary"] = DescribeSummary(PlayerSummary.Read(session, prospect.Item, ageContext)),
                ["headScan"] = DescribeHeadScan(PlayerAppearance.ReadHeadScan(session.Model, prospect.Item)),
            });
        }
        session.Guard();
        return new JsonObject { ["status"] = _prospectStatus, ["items"] = rows };
    }

    private static JsonObject DescribeHeadScan(HeadScanInfo info) => new()
    {
        ["status"] = info.Status,
        ["flag"] = info.Flag,
        ["address"] = info.Address is long address ? $"0x{address:X}" : null,
        ["error"] = info.Error,
    };

    private static JsonObject DescribeSummary(PlayerSummary summary) => new()
    {
        ["position"] = summary.Position,
        ["secondaryPosition"] = summary.SecondaryPosition,
        ["birthYear"] = summary.BirthYear,
        ["birthMonth"] = summary.BirthMonth,
        ["birthDay"] = summary.BirthDay,
        ["heightCm"] = summary.HeightCm,
        ["weightKg"] = summary.WeightKg,
        ["threePoint"] = summary.ThreePoint,
        ["age"] = summary.Age,
        ["overall"] = summary.Overall,
        ["playerType"] = summary.PlayerType,
        ["offensiveType"] = summary.OffensiveType,
    };

    private JsonNode ReadPlayer(int index, string source = "roster")
    {
        PlayerSession session = Session();
        RecordListItem item = Item(index, source);
        PlayerSnapshot snapshot = session.ReadPlayer(item);
        var buckets = new Dictionary<string, List<JsonObject>>(StringComparer.Ordinal);
        foreach (string name in PlayerSession.Sections)
        {
            buckets[name] = new List<JsonObject>();
        }
        foreach (FieldSpec spec in session.Specs.Values)
        {
            if (!snapshot.Values.TryGetValue(spec.Key, out FieldValue? value))
            {
                continue;
            }
            if (!buckets.TryGetValue(spec.Section, out List<JsonObject>? bucket))
            {
                continue;
            }
            bucket.Add(DescribeField(session, spec, value));
        }
        var sections = new JsonArray();
        foreach (string name in PlayerSession.Sections)
        {
            List<JsonObject> fields = buckets[name];
            bool appearanceSection = name is "Skin Data" or "Scanned Morph Data" or "Base Tattoo Data";
            var errors = snapshot.Errors.Where(pair => pair.Key.StartsWith(name + "/", StringComparison.Ordinal))
                .Select(pair => pair.Value).ToArray();
            bool absent = appearanceSection && fields.Count == 0 && errors.Length > 0
                && errors.All(IsAbsentObject);
            if (fields.Count == 0 && !absent)
            {
                continue;
            }
            fields.Sort((left, right) => string.CompareOrdinal(
                left["label"]!.GetValue<string>(), right["label"]!.GetValue<string>()));
            sections.Add(new JsonObject
            {
                ["name"] = name,
                ["fields"] = new JsonArray(fields.Select(field => (JsonNode?)field).ToArray()),
                ["state"] = absent ? "absent" : "loaded",
                ["message"] = absent ? "该球员无自定义外貌/纹身数据" : null,
            });
        }
        JsonObject? brand = source == "roster" ? PlayerTeamLogo(item) : null;
        return new JsonObject
        {
            ["index"] = item.Index,
            ["readToken"] = _playerReads.Store(source, new[] { snapshot }, full: true),
            ["name"] = item.Label,
            ["photoId"] = ReadPhotoId(session, item),
            ["summary"] = DescribeSummary(PlayerSummary.FromSnapshot(snapshot,
                PlayerAgeContext.Read(session.Memory), PlayerSummary.ReadOverall(session, item))),
            ["address"] = "0x" + item.Address.ToString("x", System.Globalization.CultureInfo.InvariantCulture),
            ["team"] = _teamByAddress.TryGetValue(item.Address, out string? team) ? team : string.Empty,
            ["teamLogoReference"] = brand?["logoReference"]?.DeepClone(),
            ["teamBrand"] = brand,
            ["sections"] = sections,
            ["errorCount"] = snapshot.Errors.Count,
            ["errorsBySection"] = ErrorsBySection(snapshot.Errors),
            ["unavailableCount"] = snapshot.Errors.Values.Count(IsAbsentObject),
            ["unavailableBySection"] = ErrorsBySection(snapshot.Errors
                .Where(pair => IsAbsentObject(pair.Value)).ToDictionary(pair => pair.Key, pair => pair.Value)),
            ["headScan"] = DescribeHeadScan(PlayerAppearance.HeadScanFromSnapshot(snapshot)),
        };
    }

    private static int? ReadPhotoId(PlayerSession session, RecordListItem item)
    {
        try
        {
            FieldEntry? entry = session.Model.FieldByName("Players", "PHOTOID");
            if (entry is null)
            {
                return null;
            }
            int value = Convert.ToInt32(session.Model.ReadField(entry, item).RawValue);
            return value > 0 ? value : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private JsonNode ReadPlayerStats(int index, string source)
    {
        PlayerSession session = Session();
        RecordListItem item = Item(index, source);
        var read = PlayerStatistics.Read(session, item);
        JsonArray Fields(IEnumerable<string> keys) => new(keys.Where(read.Snapshot.Values.ContainsKey)
            .Select(key => (JsonNode?)DescribeField(session, session.Specs[key], read.Snapshot.Values[key])).ToArray());
        return new JsonObject
        {
            ["index"] = item.Index,
            ["name"] = item.Label,
            ["readToken"] = _playerReads.Store(source, new[] { read.Snapshot }, full: false),
            ["direct"] = Fields(read.Snapshot.Values.Keys.Where(key => !session.Specs[key].IsSeasonBound)),
            ["seasons"] = new JsonArray(read.Seasons.Select(season => (JsonNode?)new JsonObject
            {
                ["selector"] = season.Selector,
                ["id"] = season.Id,
                ["address"] = $"0x{season.Address:X}",
                ["fields"] = Fields(season.Keys),
            }).ToArray()),
            ["warnings"] = new JsonArray(read.Warnings.Concat(read.Snapshot.Errors.Select(pair =>
                $"{pair.Key}: {pair.Value}")).Select(text => (JsonNode?)JsonValue.Create(text)).ToArray()),
        };
    }

    private static bool IsAbsentObject(string message) =>
        message.StartsWith("Parent object is not loaded:", StringComparison.Ordinal)
        || message.StartsWith("Referenced object is not loaded", StringComparison.Ordinal);

    private static JsonObject DescribeField(PlayerSession session, FieldSpec spec, FieldValue value)
    {
        string kind = value.Display switch
        {
            bool => "bool",
            string => spec.Options.Count > 0 ? "choice" : "text",
            _ => spec.Options.Count > 0 ? "choice" : "number",
        };
        var field = new JsonObject
        {
            ["key"] = spec.Key,
            ["label"] = spec.Label,
            ["section"] = spec.Section,
            ["group"] = spec.Group,
            ["value"] = ToJson(value.Display),
            ["raw"] = ToJson(value.Raw),
            ["display"] = value.Display.ToString() ?? string.Empty,
            ["writable"] = value.Writable,
            ["kind"] = kind,
            ["options"] = new JsonArray(spec.Options.Select(option => (JsonNode?)JsonValue.Create(option)).ToArray()),
        };
        if (kind == "number")
        {
            try
            {
                (double low, double high) = session.Bounds(spec);
                field["min"] = low;
                field["max"] = high;
            }
            catch (Exception)
            {
                // Bounds are advisory; a field without a declared range still edits as a number.
            }
        }
        return field;
    }

    /// <summary>
    /// Count unavailable fields per section, preserving the legacy error summary contract.
    /// </summary>
    private static JsonNode ErrorsBySection(Dictionary<string, string> errors)
    {
        var counts = new JsonObject();
        foreach (string key in errors.Keys)
        {
            int slash = key.IndexOf('/');
            string section = slash > 0 ? key[..slash] : key;
            counts[section] = (counts[section]?.GetValue<int>() ?? 0) + 1;
        }
        return counts;
    }

    private JsonNode ApplyPlayer(
        int index, JsonObject changes, string source = "roster", string? readToken = null)
    {
        PlayerSession session = Session();
        RecordListItem item = Item(index, source);
        var request = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, JsonNode?> pair in changes)
        {
            request[pair.Key] = ToClr(pair.Value);
        }
        if (request.Count == 0)
        {
            throw new InvalidOperationException("没有需要写入的字段");
        }
        if (readToken is null && request.Keys.Any(key => session.Specs.TryGetValue(key, out var spec)
            && spec.IsSeasonBound))
        {
            throw new InvalidOperationException("请先读取数据与荣誉，使用读取凭据修改赛季统计");
        }
        PlayerSnapshot snapshot = readToken is null ? session.ReadPlayer(item)
            : _playerReads.Get(readToken, source, new[] { index }, request.Keys).Single();
        PlayerTransaction? transaction = session.Apply(snapshot, request);
        _playerReads.Discard(readToken);
        var applied = new JsonArray();
        int patches = 0;
        if (transaction is not null)
        {
            patches = transaction.Patches.Count;
            foreach (PlayerTransactionRecord record in transaction.Players)
            {
                foreach (ChangeRecord change in record.Changes)
                {
                    applied.Add(new JsonObject
                    {
                        ["field"] = change.Field,
                        ["before"] = change.Before.ToString(),
                        ["after"] = change.After.ToString(),
                    });
                }
            }
        }
        return new JsonObject
        {
            ["journal"] = transaction?.JournalPath ?? string.Empty,
            ["patches"] = patches,
            ["changes"] = applied,
            ["undoDepth"] = session.UndoStack.Count,
        };
    }

    private JsonNode UndoPlayer()
    {
        PlayerSession session = Session();
        IReadOnlyList<RecordListItem> restored = session.UndoBatch();
        return new JsonObject
        {
            ["restored"] = restored.Count,
            ["undoDepth"] = session.UndoStack.Count,
        };
    }

    private static int[] BatchIndices(JsonArray indices)
    {
        int[] selected = indices.Select(node => node?.GetValue<int>()
            ?? throw new InvalidOperationException("球员索引不能为空")).ToArray();
        if (selected.Length == 0 || selected.Distinct().Count() != selected.Length)
        {
            throw new InvalidOperationException("请选择不同的球员");
        }
        return selected;
    }

    /// <summary>Read one chosen field for every selected player before enabling batch apply.</summary>
    private JsonNode ReadBatch(JsonArray indices, string field, string source = "roster")
    {
        PlayerSession session = Session();
        int[] selected = BatchIndices(indices);
        RecordListItem[] items = selected.Select(index => Item(index, source)).ToArray();
        if (!session.Specs.TryGetValue(field, out FieldSpec? spec) || !spec.Writable)
        {
            throw new InvalidOperationException("所选字段不存在或不可写");
        }
        var snapshots = new List<PlayerSnapshot>();
        var rows = new JsonArray();
        foreach (RecordListItem item in items)
        {
            PlayerSnapshot snapshot = session.ReadPlayer(item, new[] { field });
            if (!snapshot.Values.TryGetValue(field, out FieldValue? value) || !value.Writable)
            {
                throw new InvalidOperationException($"{item.Label} #{item.Index}：所选字段不可读取或不可写"
                    + (snapshot.Errors.TryGetValue(field, out string? error) ? $"（{error}）" : string.Empty));
            }
            snapshots.Add(snapshot);
            rows.Add(new JsonObject
            {
                ["index"] = item.Index,
                ["name"] = item.Label,
                ["value"] = ToJson(value.Display),
                ["display"] = value.Display.ToString() ?? string.Empty,
            });
        }
        return new JsonObject
        {
            ["readToken"] = _playerReads.Store(source, snapshots, full: false),
            ["field"] = DescribeField(session, spec, snapshots[0].Values[field]),
            ["players"] = rows,
        };
    }

    /// <summary>Apply one set of field values to several players in a single journaled transaction.</summary>
    private JsonNode ApplyBatch(
        JsonArray indices, JsonObject changes, string source = "roster", string? readToken = null)
    {
        PlayerSession session = Session();
        int[] selected = BatchIndices(indices);
        RecordListItem[] items = selected.Select(index => Item(index, source)).ToArray();
        var request = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, JsonNode?> pair in changes)
        {
            request[pair.Key] = ToClr(pair.Value);
        }
        if (request.Count == 0)
        {
            throw new InvalidOperationException("没有需要写入的字段");
        }
        var edits = new List<(PlayerSnapshot Snapshot, IReadOnlyDictionary<string, object?> Changes)>();
        IReadOnlyList<PlayerSnapshot> snapshots = readToken is null
            ? items.Select(item => session.ReadPlayer(item)).ToArray()
            : _playerReads.Get(readToken, source, selected, request.Keys);
        foreach (PlayerSnapshot snapshot in snapshots)
        {
            edits.Add((snapshot, request));
        }
        PlayerTransaction? transaction = session.ApplyBatch(edits);
        _playerReads.Discard(readToken);
        var applied = new JsonArray();
        int patches = 0;
        if (transaction is not null)
        {
            patches = transaction.Patches.Count;
            foreach (PlayerTransactionRecord record in transaction.Players)
            {
                applied.Add(new JsonObject
                {
                    ["player"] = record.Player,
                    ["changes"] = record.Changes.Count,
                });
            }
        }
        return new JsonObject
        {
            ["journal"] = transaction?.JournalPath ?? string.Empty,
            ["players"] = edits.Count,
            ["patches"] = patches,
            ["changes"] = applied,
            ["undoDepth"] = session.UndoStack.Count,
        };
    }

    /// <summary>
    /// Read the general GS array. The session is opened once: reconnecting would clear its undo
    /// stack, so later reads go through <c>Read</c> instead of <c>Connect</c>.
    /// </summary>
    private JsonNode GsRead()
    {
        _gs ??= new NativeGeneralGsSession();
        _gsSnapshot = _gs.Epoch is null ? _gs.Connect() : _gs.Read();
        return GsPayload(_gsSnapshot.Values, _gs.WriteAttempts, _gs.UndoStack.Count);
    }

    private JsonNode GsApply(JsonObject changes)
    {
        NativeGeneralGsSession session = _gs
            ?? throw new InvalidOperationException("请先读取通用 GS");
        GeneralGsSnapshot snapshot = _gsSnapshot
            ?? throw new InvalidOperationException("请先读取通用 GS");
        var request = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, JsonNode?> pair in changes)
        {
            request[pair.Key] = pair.Value?.GetValue<int>()
                ?? throw new InvalidOperationException($"字段 {pair.Key} 缺少数值");
        }
        if (request.Count == 0)
        {
            throw new InvalidOperationException("没有需要写入的 GS 字段");
        }
        GeneralGsSnapshot? fresh = session.Apply(snapshot, request);
        _gsSnapshot = fresh ?? session.Read();
        return GsPayload(_gsSnapshot.Values, session.WriteAttempts, session.UndoStack.Count);
    }

    private JsonNode GsUndo()
    {
        NativeGeneralGsSession session = _gs
            ?? throw new InvalidOperationException("请先读取通用 GS");
        _gsSnapshot = session.Undo();
        return GsPayload(_gsSnapshot.Values, session.WriteAttempts, session.UndoStack.Count);
    }

    /// <summary>
    /// Read the in-match GS array. It now carries the same 70 fields as general GS, so both
    /// tabs share one payload shape.
    /// </summary>
    private JsonNode InMatchRead()
    {
        _inMatch ??= new NativeMenuGsSession();
        _inMatchSnapshot = _inMatch.Epoch is null ? _inMatch.Connect() : _inMatch.Read();
        var result = GsPayload(_inMatchSnapshot.AllZero ? new() : _inMatchSnapshot.Values,
            _inMatch.WriteAttempts, _inMatch.UndoStack.Count);
        if (_inMatchSnapshot.AllZero)
            result["notice"] = "当前比赛设置尚未加载，请进入比赛后重新读取。";
        return result;
    }

    private JsonNode InMatchApply(JsonObject changes)
    {
        NativeMenuGsSession session = _inMatch
            ?? throw new InvalidOperationException("请先读取场内 GS");
        LegacyGsSnapshot snapshot = _inMatchSnapshot
            ?? throw new InvalidOperationException("请先读取场内 GS");
        int applied = 0;
        foreach (KeyValuePair<string, JsonNode?> pair in changes)
        {
            int value = pair.Value?.GetValue<int>()
                ?? throw new InvalidOperationException($"字段 {pair.Key} 缺少数值");
            snapshot = session.ApplyField(snapshot, pair.Key, value);
            applied++;
        }
        if (applied == 0)
        {
            throw new InvalidOperationException("没有需要写入的 GS 字段");
        }
        _inMatchSnapshot = snapshot;
        return GsPayload(snapshot.Values, session.WriteAttempts, session.UndoStack.Count);
    }

    private JsonNode InMatchUndo()
    {
        NativeMenuGsSession session = _inMatch
            ?? throw new InvalidOperationException("请先读取场内 GS");
        _inMatchSnapshot = session.Undo();
        return GsPayload(_inMatchSnapshot.Values, session.WriteAttempts, session.UndoStack.Count);
    }

    /// <summary>One payload shape for both GS tabs, labelled from the shared preset catalog.</summary>
    private static JsonNode GsPayload(Dictionary<string, int> values, int writeAttempts, int undoDepth)
    {
        var fields = new JsonArray();
        foreach (GsField field in GsPresets.Catalog)
        {
            fields.Add(new JsonObject
            {
                ["id"] = field.Id,
                ["label"] = field.Label,
                ["group"] = field.Group,
                ["user"] = Percent(values, $"user.{field.Id}"),
                ["cpu"] = Percent(values, $"cpu.{field.Id}"),
            });
        }
        var groups = new JsonArray();
        foreach (KeyValuePair<string, string> pair in GsPresets.Groups)
        {
            groups.Add(new JsonObject { ["id"] = pair.Key, ["label"] = pair.Value });
        }
        var sides = new JsonArray();
        foreach (string side in GsPresets.Sides)
        {
            sides.Add(new JsonObject { ["id"] = side, ["label"] = GsPresets.SideLabels[side] });
        }
        return new JsonObject
        {
            ["fields"] = fields,
            ["groups"] = groups,
            ["sides"] = sides,
            ["undoDepth"] = undoDepth,
            ["writeAttempts"] = writeAttempts,
        };
    }

    private static JsonNode? Percent(Dictionary<string, int> values, string key) =>
        values.TryGetValue(key, out int value) ? JsonValue.Create(value) : null;

    private RosterMover? _mover;
    private List<(RecordListItem Team, TeamSlots Slots)>? _teamOptions;

    private RosterMover Mover()
    {
        PlayerSession session = Session();
        return _mover ??= new RosterMover(session);
    }

    /// <summary>Team list with live slot usage; invalidated after every move so counts stay true.</summary>
    private List<(RecordListItem Team, TeamSlots Slots)> TeamOptions()
    {
        return _teamOptions ??= Mover().TeamOptions().ToList();
    }

    private RecordListItem Item(int index, string source = "roster")
    {
        if (source == "prospects")
        {
            if (!_prospectByIndex.TryGetValue(index, out ProspectInfo? prospect))
            {
                throw new InvalidOperationException($"没有索引为 {index} 的待选秀球员，请刷新名单");
            }
            PlayerSession session = Session();
            session.Guard();
            ProspectCatalog current = ProspectBrowser.Read(session.Model, new[] { prospect.Item });
            if (current.Records.Count != 1 || !current.Records[0].IsProspect)
            {
                throw new InvalidOperationException("待选秀球员状态已变化，请刷新名单");
            }
            return prospect.Item;
        }
        Dictionary<int, RecordListItem> map = source switch
        {
            "roster" => _byIndex,
            "draft" => _draftByIndex,
            _ => throw new InvalidOperationException($"未知球员来源：{source}"),
        };
        return map.TryGetValue(index, out RecordListItem? item)
            ? item
            : throw new InvalidOperationException(
                $"没有索引为 {index} 的{(source == "draft" ? "新秀" : "球员")}，请重新连接");
    }

    /// <summary>The draft class table; the game leaves it unloaded outside the draft flow.</summary>
    private JsonNode Rookies()
    {
        PlayerSession session = Session();
        session.Guard();
        if (session.RosterIndex.DraftItems.Count > 0)
        {
            session.GuardEdit(session.Epoch!, "draft", session.RosterIndex.DraftSignature);
        }
        PlayerAgeContext ageContext = PlayerAgeContext.Read(session.Memory);
        var rows = new JsonArray();
        foreach (RecordListItem item in session.RosterIndex.DraftItems)
        {
            rows.Add(new JsonObject
            {
                ["key"] = item.Index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["index"] = item.Index,
                ["name"] = item.Label,
                ["team"] = string.Empty,
                ["photoId"] = ReadPhotoId(session, item),
                ["summary"] = DescribeSummary(PlayerSummary.Read(session, item, ageContext)),
            });
        }
        session.Guard();
        if (session.RosterIndex.DraftItems.Count > 0)
        {
            session.GuardEdit(session.Epoch!, "draft", session.RosterIndex.DraftSignature);
        }
        return new JsonObject
        {
            ["status"] = session.RosterIndex.DraftStatus,
            ["items"] = rows,
        };
    }

    private RecordListItem Team(int teamIndex)
    {
        foreach ((RecordListItem team, TeamSlots _) in TeamOptions())
        {
            if (team.Index == teamIndex)
            {
                return team;
            }
        }
        throw new InvalidOperationException($"没有索引为 {teamIndex} 的球队");
    }

    private JsonNode Teams()
    {
        var rows = new JsonArray();
        var indices = LeagueTeams().Select(team => team.Index).ToHashSet();
        foreach ((RecordListItem team, TeamSlots slots) in TeamOptions().Where(row => indices.Contains(row.Team.Index)))
        {
            JsonObject row = TeamBranding(team);
            row["index"] = team.Index;
            row["label"] = team.Label;
            row["used"] = slots.Used;
            row["capacity"] = slots.Values.Length;
            row["open"] = new JsonArray(slots.OpenSlots
                .Select(slot => (JsonNode?)JsonValue.Create(slot)).ToArray());
            rows.Add(row);
        }
        return rows;
    }

    private JsonNode Placement(int index)
    {
        RecordListItem item = Item(index);
        Placement placement = Mover().Placement(item);
        var result = new JsonObject
        {
            ["team"] = placement.TeamLabel,
            ["teamIndex"] = placement.TeamIndex,
            ["slot"] = placement.Slot,
        };
        // Read-only extras for the trade view; missing fields leave the keys out.
        Schema model = Session().Model;
        try
        {
            FieldEntry? salary = model.FieldByName("Players", "SALARYYEAR1");
            FieldEntry? years = model.FieldByName("Players", "YEARSLEFT");
            FieldEntry? payroll = model.FieldByName("Teams", "SALARY");
            if (salary is not null)
            {
                result["salary"] = Conversions.ToLong(model.ReadField(salary, item).RawValue);
            }
            if (years is not null)
            {
                result["yearsLeft"] = Conversions.ToLong(model.ReadField(years, item).RawValue);
            }
            if (payroll is not null)
            {
                result["teamPayroll"] = Conversions.ToLong(model.ReadField(payroll, Team(placement.TeamIndex)).RawValue);
            }
        }
        catch (Exception exc) when (exc is InvalidOperationException or ArgumentException)
        {
            result["extrasError"] = exc.Message;
        }
        return result;
    }

    /// <summary>Payroll sync defaults off so callers that predate it keep their exact writes.</summary>
    private static bool SyncPayroll(JsonObject payload) =>
        payload["syncPayroll"] is JsonValue value && value.GetValue<bool>();

    private static int[] PickList(JsonObject payload, string key)
    {
        if (payload[key] is not JsonArray items)
        {
            return Array.Empty<int>();
        }
        if (items.Count > 20)
        {
            throw new InvalidOperationException("一次最多附带 20 个选秀权");
        }
        return items.Select(item => item?.GetValue<int>()
            ?? throw new InvalidOperationException("选秀权编号无效")).ToArray();
    }

    /// <summary>Owned, decodable draft picks; read-only.</summary>
    private JsonNode DraftPicks()
    {
        PlayerSession session = Session();
        session.Guard();
        var teamIndex = TeamOptions().ToDictionary(row => row.Team.Address, row => row.Team);
        var rows = new JsonArray();
        foreach (DraftPickView view in DraftPickTable.Describe(session.Model, session.Items))
        {
            DraftPick pick = view.Pick;
            if (!teamIndex.TryGetValue(pick.Owner, out RecordListItem? owner))
            {
                continue;
            }
            rows.Add(new JsonObject
            {
                ["index"] = pick.Index,
                ["ownerIndex"] = owner.Index,
                ["owner"] = owner.Label,
                ["originalIndex"] = teamIndex.TryGetValue(pick.Original, out RecordListItem? original) ? original.Index : null,
                ["original"] = original?.Label,
                ["year"] = view.Year,
                ["round"] = pick.Round,
                ["order"] = pick.Order,
                ["used"] = pick.Used,
                ["player"] = view.PlayerLabel,
                ["hasTerms"] = view.HasTerms,
            });
        }
        session.Guard();
        return rows;
    }

    private JsonNode PlanMove(int index, int teamIndex, bool syncPayroll)
    {
        MovePlan plan = Mover().PlanMove(Item(index), Team(teamIndex), syncPayroll);
        return DescribePlan(plan, applied: false, patches: 0, journal: string.Empty);
    }

    private JsonNode ApplyMove(int index, int teamIndex, bool syncPayroll)
    {
        RosterMover mover = Mover();
        MovePlan plan = mover.PlanMove(Item(index), Team(teamIndex), syncPayroll);
        RosterMoveTransaction transaction = mover.Apply(plan);
        _teamOptions = null;
        return DescribePlan(plan, applied: true, transaction.Patches.Count, transaction.JournalPath);
    }

    private JsonNode PlanTrade(int first, int second, int[] firstPicks, int[] secondPicks, bool syncPayroll)
    {
        MovePlan plan = Mover().PlanTrade(Item(first), Item(second), firstPicks, secondPicks, syncPayroll);
        return DescribePlan(plan, applied: false, patches: 0, journal: string.Empty);
    }

    private JsonNode ApplyTrade(int first, int second, int[] firstPicks, int[] secondPicks, bool syncPayroll)
    {
        RosterMover mover = Mover();
        MovePlan plan = mover.PlanTrade(Item(first), Item(second), firstPicks, secondPicks, syncPayroll);
        RosterMoveTransaction transaction = mover.Apply(plan);
        _teamOptions = null;
        return DescribePlan(plan, applied: true, transaction.Patches.Count, transaction.JournalPath);
    }

    private JsonNode UndoMove()
    {
        RosterMoveTransaction transaction = Mover().Undo();
        _teamOptions = null;
        return new JsonObject
        {
            ["kind"] = transaction.Kind,
            ["summary"] = transaction.Summary,
            ["applied"] = true,
            ["undo"] = true,
            ["patchCount"] = transaction.Patches.Count,
            ["journal"] = transaction.JournalPath ?? string.Empty,
        };
    }

    private static JsonNode DescribePlan(MovePlan plan, bool applied, int patches, string? journal) =>
        new JsonObject
        {
            ["kind"] = plan.Kind,
            ["summary"] = plan.Summary,
            ["patchCount"] = applied ? patches : plan.Rows.Count,
            ["playerCount"] = plan.Players.Count,
            ["teamCount"] = plan.Teams.Count,
            ["applied"] = applied,
            ["journal"] = journal ?? string.Empty,
            ["details"] = new JsonArray(plan.Details.Select(text => (JsonNode?)JsonValue.Create(text)).ToArray()),
        };

    /// <summary>GS presets live beside the journals so they survive reinstalls of the EXE.</summary>
    private string PresetDirectory()
    {
        string root = _session?.DataDirectory
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "NBA2K27PlayerEditor");
        return Path.Combine(root, "presets");
    }

    /// <summary>Reject anything that could escape the preset directory.</summary>
    private string PresetPath(string name)
    {
        string trimmed = (name ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            throw new InvalidOperationException("预设名称不能为空");
        }
        if (trimmed.IndexOfAny(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }) >= 0
            || trimmed.Contains("..", StringComparison.Ordinal)
            || trimmed.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new InvalidOperationException("预设名称不能包含路径或非法字符");
        }
        if (!trimmed.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            trimmed += ".json";
        }
        return Path.Combine(PresetDirectory(), trimmed);
    }

    private JsonNode PresetList()
    {
        string directory = PresetDirectory();
        Directory.CreateDirectory(directory);
        var files = new JsonArray();
        foreach (string path in Directory.EnumerateFiles(directory, "*.json")
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            files.Add(new JsonObject
            {
                ["name"] = Path.GetFileName(path),
                ["bytes"] = new FileInfo(path).Length,
                ["modified"] = File.GetLastWriteTime(path)
                    .ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture),
            });
        }
        return new JsonObject { ["directory"] = directory, ["files"] = files };
    }

    private JsonNode PresetLoad(string name)
    {
        Dictionary<string, int> values = GsPresets.LoadPreset(PresetPath(name));
        var rows = new JsonArray();
        foreach (KeyValuePair<string, int> pair in values)
        {
            rows.Add(new JsonObject { ["key"] = pair.Key, ["value"] = pair.Value });
        }
        return new JsonObject
        {
            ["name"] = name,
            ["count"] = values.Count,
            ["values"] = rows,
        };
    }

    private JsonNode PresetSave(string name, JsonObject values)
    {
        var payload = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, JsonNode?> pair in values)
        {
            payload[pair.Key] = pair.Value?.GetValue<int>()
                ?? throw new InvalidOperationException($"字段 {pair.Key} 缺少数值");
        }
        string saved = GsPresets.SavePreset(PresetPath(name), payload, overwrite: true);
        return new JsonObject
        {
            ["name"] = Path.GetFileName(saved),
            ["path"] = saved,
            ["count"] = payload.Count,
        };
    }

    private JsonNode PresetDelete(string name)
    {
        string path = PresetPath(name);
        bool existed = File.Exists(path);
        if (existed)
        {
            File.Delete(path);
        }
        return new JsonObject { ["name"] = Path.GetFileName(path), ["deleted"] = existed };
    }

    /// <summary>
    /// Write every writable field of one player to a JSON backup under the editor data folder.
    /// This reads the game and writes only our own file.
    /// </summary>
    private JsonNode ExportPlayer(int index, string source = "roster")
    {
        PlayerSession session = Session();
        RecordListItem item = Item(index, source);
        PlayerSnapshot snapshot = session.ReadPlayer(item);
        var values = new JsonObject();
        int count = 0;
        foreach (KeyValuePair<string, FieldValue> pair in snapshot.Values)
        {
            if (!pair.Value.Writable)
            {
                continue;
            }
            values[pair.Key] = ToJson(pair.Value.Raw);
            count++;
        }
        string directory = Path.Combine(session.DataDirectory, "exports");
        Directory.CreateDirectory(directory);
        string safe = new(item.Label.Where(character => !Path.GetInvalidFileNameChars().Contains(character)).ToArray());
        safe = safe.Trim();
        if (safe.Length == 0)
        {
            safe = "player";
        }
        string path = Path.Combine(directory,
            $"{safe}-{DateTime.Now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture)}.json");
        var document = new JsonObject
        {
            ["format"] = "nba2k27-player-export/1",
            ["source"] = source,
            ["time"] = DateTimeOffset.Now.ToString("yyyy-MM-ddTHH:mm:sszzz",
                System.Globalization.CultureInfo.InvariantCulture),
            ["index"] = item.Index,
            ["name"] = item.Label,
            ["address"] = "0x" + item.Address.ToString("x", System.Globalization.CultureInfo.InvariantCulture),
            ["fieldCount"] = count,
            ["values"] = values,
        };
        File.WriteAllText(path, document.ToJsonString(Program.JsonOptions) + "\n",
            new System.Text.UTF8Encoding(false));
        return new JsonObject { ["path"] = path, ["name"] = item.Label, ["count"] = count };
    }

    /// <summary>
    /// Apply a previously exported player backup onto this player. Only fields that are writable
    /// and present in both the file and this record are touched.
    /// </summary>
    private JsonNode ImportPlayer(int index, string source = "roster", string? readToken = null, bool preview = false)
    {
        PlayerSession session = Session();
        RecordListItem item = Item(index, source);
        PlayerSnapshot snapshot = readToken is null ? session.ReadPlayer(item)
            : _playerReads.Get(readToken, source, new[] { index }).Single();
        session.Guard(snapshot.Epoch);
        string? path = PickFile?.Invoke("球员备份|*.json");
        if (string.IsNullOrEmpty(path))
        {
            return new JsonObject { ["cancelled"] = true, ["applied"] = 0 };
        }
        JsonObject document;
        try
        {
            document = JsonNode.Parse(File.ReadAllText(path))?.AsObject()
                ?? throw new InvalidOperationException("备份文件为空");
        }
        catch (Exception exc) when (exc is IOException or System.Text.Json.JsonException)
        {
            throw new InvalidOperationException("无法读取备份文件：" + exc.Message);
        }
        if (document["values"] is not JsonObject values)
        {
            throw new InvalidOperationException("备份文件缺少 values 字段");
        }
        var changes = new Dictionary<string, object?>(StringComparer.Ordinal);
        var checkedFields = new List<string>();
        var listed = new JsonArray();
        var requestChanges = new JsonObject();
        int skipped = 0;
        foreach (KeyValuePair<string, JsonNode?> pair in values)
        {
            if (!session.Specs.TryGetValue(pair.Key, out FieldSpec? spec)
                || !snapshot.Values.TryGetValue(pair.Key, out FieldValue? current)
                || !current.Writable)
            {
                skipped++;
                continue;
            }
            checkedFields.Add(pair.Key);
            object? raw = ToClr(pair.Value);
            if (raw is null || PlayerPatchGuard.SameValue(raw, current.Raw))
            {
                skipped++;
                continue;
            }
            object decoded = session.Decode(spec, raw);
            changes[pair.Key] = decoded;
            if (preview)
            {
                requestChanges[pair.Key] = ToJson(decoded);
                listed.Add(new JsonObject
                {
                    ["field"] = pair.Key, ["label"] = spec.Label,
                    ["before"] = ToJson(current.Raw), ["after"] = pair.Value?.DeepClone(),
                    ["beforeField"] = DescribeField(session, spec, current),
                    ["afterField"] = DescribeField(session, spec, current with { Raw = raw, Display = decoded }),
                });
            }
        }
        session.ValidateSnapshot(snapshot, checkedFields);
        if (preview)
        {
            return new JsonObject
            {
                ["cancelled"] = false, ["index"] = index, ["source"] = source,
                ["fileName"] = Path.GetFileName(path), ["changeCount"] = changes.Count, ["skipped"] = skipped,
                ["changes"] = listed, ["requestChanges"] = requestChanges,
                ["readToken"] = _playerReads.Store(source, new[] { snapshot }, full: true),
            };
        }
        if (changes.Count == 0)
        {
            return new JsonObject
            {
                ["cancelled"] = false,
                ["applied"] = 0,
                ["skipped"] = skipped,
                ["source"] = Path.GetFileName(path),
                ["undoDepth"] = session.UndoStack.Count,
            };
        }
        PlayerTransaction? transaction = session.Apply(snapshot, changes);
        _playerReads.Discard(readToken);
        return new JsonObject
        {
            ["cancelled"] = false,
            ["applied"] = changes.Count,
            ["skipped"] = skipped,
            ["patches"] = transaction?.Patches.Count ?? 0,
            ["journal"] = transaction?.JournalPath ?? string.Empty,
            ["source"] = Path.GetFileName(path),
            ["undoDepth"] = session.UndoStack.Count,
        };
    }

    // ---- custom player and face library ------------------------------------
    private string LibraryDirectory()
    {
        string root = _session?.DataDirectory
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "NBA2K27PlayerEditor");
        return Path.Combine(root, "library");
    }

    private static JsonObject DescribeEntry(LibraryEntry entry) => new()
    {
        ["id"] = entry.Id, ["name"] = entry.Name, ["kind"] = entry.Kind, ["created"] = entry.Created,
        ["sourcePlayer"] = entry.SourcePlayer, ["fieldCount"] = entry.FieldCount, ["notes"] = entry.Notes,
    };

    private JsonNode LibraryList() =>
        new JsonArray(PlayerLibrary.List(LibraryDirectory()).Select(entry => (JsonNode?)DescribeEntry(entry)).ToArray());

    /// <summary>Save the current player's writable fields (all or face only); reads the game only.</summary>
    private JsonNode LibrarySave(int index, string kind, string name, string notes)
    {
        PlayerSession session = Session();
        RecordListItem item = Item(index);
        PlayerSnapshot snapshot = session.ReadPlayer(item);
        var values = snapshot.Values.Where(pair => pair.Value.Writable)
            .Select(pair => new KeyValuePair<string, JsonNode?>(pair.Key, ToJson(pair.Value.Raw)));
        DateTimeOffset now = DateTimeOffset.Now;
        JsonObject document = PlayerLibrary.Create(PlayerLibrary.NewId(now), name, kind, item.Label, notes, values, now);
        PlayerLibrary.Save(LibraryDirectory(), document);
        return DescribeEntry(PlayerLibrary.Parse(document.ToJsonString(), document["id"]!.GetValue<string>()).Entry);
    }

    private (LibraryEntry Entry, JsonObject Values) LoadEntry(string id)
    {
        string path = PlayerLibrary.PathFor(LibraryDirectory(), id);
        if (!File.Exists(path))
        {
            throw new InvalidOperationException("库中没有这个条目，请刷新");
        }
        return PlayerLibrary.Parse(File.ReadAllText(path), id);
    }

    /// <summary>
    /// Apply a library entry onto a roster player through the normal journaled player edit. With
    /// preview the changes are only listed. Scope "face" limits a player entry to its face fields.
    /// </summary>
    private JsonNode LibraryApply(string id, int index, string scope, string? readToken, bool preview)
    {
        var (entry, values) = LoadEntry(id);
        if (scope == "player" && entry.Kind == "face")
        {
            throw new InvalidOperationException("面部条目只能按面部应用");
        }
        PlayerSession session = Session();
        RecordListItem item = Item(index);
        PlayerSnapshot snapshot = readToken is null || preview ? session.ReadPlayer(item)
            : _playerReads.Get(readToken, "roster", new[] { index }).Single();
        session.Guard(snapshot.Epoch);
        var changes = new Dictionary<string, object?>(StringComparer.Ordinal);
        var checkedFields = new List<string>();
        var listed = new JsonArray();
        int skipped = 0;
        foreach (KeyValuePair<string, JsonNode?> pair in values)
        {
            if (!PlayerLibrary.InScope(pair.Key, scope)
                || !session.Specs.TryGetValue(pair.Key, out FieldSpec? spec)
                || !snapshot.Values.TryGetValue(pair.Key, out FieldValue? current)
                || !current.Writable)
            {
                skipped++;
                continue;
            }
            checkedFields.Add(pair.Key);
            object? raw = ToClr(pair.Value);
            if (raw is null || PlayerPatchGuard.SameValue(raw, current.Raw))
            {
                continue;
            }
            object decoded = session.Decode(spec, raw);
            changes[pair.Key] = decoded;
            listed.Add(new JsonObject
            {
                ["field"] = pair.Key, ["label"] = spec.Label,
                ["before"] = ToJson(current.Raw), ["after"] = pair.Value?.DeepClone(),
                ["beforeField"] = DescribeField(session, spec, current),
                ["afterField"] = DescribeField(session, spec, current with { Raw = raw, Display = decoded }),
            });
        }
        var result = new JsonObject
        {
            ["entry"] = DescribeEntry(entry), ["index"] = index, ["player"] = item.Label, ["scope"] = scope,
            ["changeCount"] = changes.Count, ["skipped"] = skipped, ["changes"] = listed, ["applied"] = false,
        };
        if (preview || changes.Count == 0)
        {
            return result;
        }
        session.ValidateSnapshot(snapshot, checkedFields);
        PlayerTransaction? transaction = session.Apply(snapshot, changes);
        _playerReads.Discard(readToken);
        result["applied"] = true;
        result["patches"] = transaction?.Patches.Count ?? 0;
        result["journal"] = transaction?.JournalPath ?? string.Empty;
        result["undoDepth"] = session.UndoStack.Count;
        return result;
    }

    private JsonNode LibraryDelete(string id)
    {
        string path = PlayerLibrary.PathFor(LibraryDirectory(), id);
        bool existed = File.Exists(path);
        if (existed)
        {
            File.Delete(path);
        }
        return new JsonObject { ["id"] = id, ["deleted"] = existed };
    }

    /// <summary>Copy a library entry or a player export file into the library.</summary>
    private JsonNode LibraryImport()
    {
        string? path = PickFile?.Invoke("球员库或球员备份|*.json");
        if (string.IsNullOrEmpty(path))
        {
            return new JsonObject { ["cancelled"] = true };
        }
        DateTimeOffset now = DateTimeOffset.Now;
        string id = PlayerLibrary.NewId(now);
        var (entry, values) = PlayerLibrary.Parse(File.ReadAllText(path), id);
        JsonObject document = PlayerLibrary.Create(id, entry.Name, entry.Kind, entry.SourcePlayer,
            entry.Notes, values.Select(pair => new KeyValuePair<string, JsonNode?>(pair.Key, pair.Value)), now);
        PlayerLibrary.Save(LibraryDirectory(), document);
        JsonObject described = DescribeEntry(PlayerLibrary.Parse(document.ToJsonString(), id).Entry);
        described["cancelled"] = false;
        return described;
    }

    /// <summary>Most recent journal-style files across every folder this editor writes.</summary>
    private JsonNode JournalList()
    {
        PlayerSession session = Session();
        var rows = new JsonArray();
        (string Label, string Directory)[] folders =
        {
            ("球员事务", session.DataDirectory),
            ("通用 GS", Path.Combine(session.DataDirectory, "general-gs")),
            ("场内 GS", Path.Combine(session.DataDirectory, "native-gs")),
            ("预设", Path.Combine(session.DataDirectory, "presets")),
            ("球员导出", Path.Combine(session.DataDirectory, "exports")),
            ("球员库", Path.Combine(session.DataDirectory, "library")),
            ("历史编辑", Path.Combine(session.DataDirectory, "history-edits")),
            ("球队编辑", Path.Combine(session.DataDirectory, "team-edits")),
        };
        foreach ((string label, string directory) in folders)
        {
            if (!Directory.Exists(directory))
            {
                continue;
            }
            foreach (string path in Directory.EnumerateFiles(directory, "*.json")
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .Take(40))
            {
                rows.Add(new JsonObject
                {
                    ["kind"] = label,
                    ["name"] = Path.GetFileName(path),
                    ["directory"] = directory,
                    ["bytes"] = new FileInfo(path).Length,
                    ["modified"] = File.GetLastWriteTime(path).ToString(
                        "yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture),
                    ["detail"] = JournalReader.Read(directory, Path.GetFileName(path)),
                });
            }
        }
        return rows;
    }

    /// <summary>
    /// Local portrait overrides. The game stores no photos, so an avatar is either a user-supplied
    /// image cached here or the initials placeholder the page draws itself.
    /// </summary>
    private string AvatarDirectory()
    {
        string root = _session?.DataDirectory
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "NBA2K27PlayerEditor");
        return Path.Combine(root, "avatars");
    }

    private static string AvatarKey(string key)
    {
        var builder = new System.Text.StringBuilder();
        foreach (char character in key ?? string.Empty)
        {
            if (Path.GetInvalidFileNameChars().Contains(character) || character is '.' or '\\' or '/')
            {
                continue;
            }
            builder.Append(character);
        }
        string safe = builder.ToString().Trim();
        if (safe.Length == 0)
        {
            safe = "player";
        }
        return safe.Length > 80 ? safe[..80] : safe;
    }

    private string AvatarPath(string key) => Path.Combine(AvatarDirectory(), AvatarKey(key) + ".png");

    private JsonNode AvatarGet(string key)
    {
        string path = AvatarPath(key);
        if (!File.Exists(path))
        {
            return new JsonObject { ["key"] = key, ["found"] = false };
        }
        return new JsonObject
        {
            ["key"] = key,
            ["found"] = true,
            ["dataUrl"] = "data:image/png;base64," + Convert.ToBase64String(File.ReadAllBytes(path)),
        };
    }

    private JsonNode AvatarPick(string key)
    {
        string? source = PickFile?.Invoke("图片|*.png;*.jpg;*.jpeg;*.webp;*.bmp;*.gif");
        if (string.IsNullOrEmpty(source))
        {
            return new JsonObject { ["key"] = key, ["found"] = false, ["cancelled"] = true };
        }
        Directory.CreateDirectory(AvatarDirectory());
        string path = AvatarPath(key);
        using (System.Drawing.Image image = System.Drawing.Image.FromFile(source))
        {
            // Center-crop to a square and shrink, so the cache stays small and looks uniform.
            const int size = 128;
            int side = Math.Min(image.Width, image.Height);
            int left = (image.Width - side) / 2;
            int top = (image.Height - side) / 2;
            using var thumb = new System.Drawing.Bitmap(size, size);
            using (System.Drawing.Graphics graphics = System.Drawing.Graphics.FromImage(thumb))
            {
                graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                graphics.DrawImage(image, new System.Drawing.Rectangle(0, 0, size, size),
                    new System.Drawing.Rectangle(left, top, side, side), System.Drawing.GraphicsUnit.Pixel);
            }
            thumb.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        }
        return AvatarGet(key);
    }

    private JsonNode AvatarClear(string key)
    {
        string path = AvatarPath(key);
        bool existed = File.Exists(path);
        if (existed)
        {
            File.Delete(path);
        }
        return new JsonObject { ["key"] = key, ["found"] = false, ["deleted"] = existed };
    }

    private List<RecordListItem>? _teamItems;
    private long _teamTable;

    private List<RecordListItem> TeamItems()
    {
        PlayerSession session = Session();
        session.Guard();
        long table = session.Model.BaseAddressForKey("Team");
        if (_teamItems is not null && table != _teamTable)
        {
            throw new InvalidOperationException("球队表已变化，请重新连接");
        }
        if (_teamItems is null)
        {
            List<RecordListItem> items = session.Model.ScanRecords("Teams");
            session.Guard();
            if (table <= 0 || session.Model.BaseAddressForKey("Team") != table)
            {
                throw new InvalidOperationException("读取期间球队表已变化，请重新连接");
            }
            _teamTable = table;
            _teamItems = items;
        }
        return _teamItems;
    }

    private JsonNode ListTeams(string scope)
    {
        if (scope is not ("league" or "all")) throw new ArgumentException("未知球队范围");
        var rows = new JsonArray();
        foreach (RecordListItem team in scope == "all" ? TeamItems() : LeagueTeams())
        {
            JsonObject row = TeamBranding(team);
            row["key"] = team.Index.ToString(System.Globalization.CultureInfo.InvariantCulture);
            row["index"] = team.Index;
            rows.Add(row);
        }
        return rows;
    }

    private JsonObject TeamBranding(RecordListItem team, Dictionary<string, object?>? values = null)
    {
        PlayerSession session = Session();
        if (values is null)
        {
            values = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (FieldEntry entry in session.Model.GroupedFields("Teams").Where(field =>
                field.NormalizedName is "LOGO2" or "LOGO1" or "BASELOGONAME" or "HISTORICYEAR"))
            {
                try
                {
                    values[entry.NormalizedName] = session.Model.ReadField(entry, team).RawValue;
                }
                catch (Exception)
                {
                    // Unavailable metadata stays absent; it must not become a modern season.
                }
            }
        }
        string[] references = new[] { "LOGO2", "BASELOGONAME", "LOGO1" }
            .Select(name => values.GetValueOrDefault(name)?.ToString())
            .Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!).ToArray();
        int? historicYear = values.ContainsKey("HISTORICYEAR")
            ? Conversions.ToInt(values["HISTORICYEAR"]) : null;
        int? inheritedFrom = null;
        // All-time records may omit their branding. Reuse a matching current record
        // from this live table only when this record has no historical season.
        if (references.Length == 0 && historicYear == 0)
        {
            FieldEntry? baseLogo = session.Model.GroupedFields("Teams")
                .FirstOrDefault(field => field.NormalizedName == "BASELOGONAME");
            if (baseLogo is not null)
            {
                foreach (RecordListItem candidate in TeamItems().Where(item =>
                    item.Index != team.Index && item.Label == team.Label))
                {
                    try
                    {
                        string? value = session.Model.ReadField(baseLogo, candidate).RawValue?.ToString();
                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            references = new[] { value };
                            inheritedFrom = candidate.Index;
                            break;
                        }
                    }
                    catch (Exception)
                    {
                        // A missing candidate does not establish an inherited identity.
                    }
                }
            }
        }
        return new JsonObject
        {
            ["label"] = string.IsNullOrWhiteSpace(team.Label) ? $"球队 {team.Index}" : team.Label,
            ["logoReference"] = references.FirstOrDefault(),
            ["logoReferences"] = new JsonArray(references.Select(value => (JsonNode?)JsonValue.Create(value)).ToArray()),
            ["historicYearRaw"] = historicYear,
            ["logoInheritedFromIndex"] = inheritedFrom,
        };
    }

    private JsonObject? PlayerTeamLogo(RecordListItem player)
    {
        var bucket = Session().RosterIndex.Teams.FirstOrDefault(team =>
            team.Players.Any(item => item.Address == player.Address));
        var record = bucket is null ? null : TeamItems().FirstOrDefault(team => team.Index == bucket.Index);
        return record is null ? null : TeamBranding(record);
    }

    private static bool IsTeamTextReference(FieldEntry entry) =>
        entry.NormalizedName is "LOCALIZEDCITYNAME" or "LOCALIZEDSHORTTEAMNAME" or "LOCALIZEDSTATENAME";

    /// <summary>
    /// Read one team record through the shared schema reader. This is the same code path players
    /// use, so every implemented field type decodes here too.
    /// </summary>
    private JsonNode ReadTeam(int index)
    {
        PlayerSession session = Session();
        List<RecordListItem> teams = TeamItems();
        RecordListItem team = RecordEditScope.FindByIndex(teams, index);
        var scope = new RecordEditScope(session.Epoch!, _teamTable, session.Model.DomainStride("Teams"), team);
        CheckTeamScope(session, scope);
        var buckets = new Dictionary<string, List<JsonObject>>(StringComparer.Ordinal);
        var playersByAddress = _byIndex.Values.GroupBy(item => item.Address)
            .ToDictionary(group => group.Key, group => group.First());
        var brandValues = new Dictionary<string, object?>(StringComparer.Ordinal);
        var order = new List<string>();
        foreach (FieldEntry entry in session.Model.GroupedFields("Teams"))
        {
            if (HistorySession.IsIndependentTeamField(entry, session.Model.FieldVersionPayload(entry.Field)))
            {
                continue;
            }
            FieldRead read;
            try
            {
                read = session.Model.ReadField(entry, team);
            }
            catch (Exception)
            {
                // A field whose parent pointer is absent simply has no value for this record.
                continue;
            }
            if (entry.NormalizedName is "LOGO2" or "LOGO1" or "BASELOGONAME" or "HISTORICYEAR")
            {
                brandValues[entry.NormalizedName] = read.RawValue;
            }
            if (!buckets.TryGetValue(entry.Section, out List<JsonObject>? bucket))
            {
                bucket = new List<JsonObject>();
                buckets[entry.Section] = bucket;
                order.Add(entry.Section);
            }
            JsonObject payload = session.Model.FieldVersionPayload(entry.Field);
            var options = new JsonArray();
            if ((payload["values"] ?? payload["dropdown"]) is JsonArray optionArray)
            {
                foreach (JsonNode? option in optionArray)
                {
                    options.Add(FieldIo.NodeText(option));
                }
            }
            bool textReference = IsTeamTextReference(entry);
            string display = read.DisplayValue?.ToString() ?? string.Empty;
            var field = new JsonObject
            {
                ["key"] = $"{entry.Section}/{entry.Group}/{entry.NormalizedName}",
                ["label"] = string.IsNullOrWhiteSpace(entry.DisplayName) ? entry.NormalizedName : entry.DisplayName,
                ["section"] = entry.Section,
                ["group"] = entry.Group,
                ["value"] = textReference ? JsonValue.Create(display)
                    : options.Count > 0 ? ToJson(read.DisplayValue) : ToJson(read.RawValue),
                ["display"] = display,
                ["writable"] = read.Writeable && !textReference,
                ["options"] = options,
                ["kind"] = textReference ? "text" : options.Count > 0
                    ? "choice"
                    : read.RawValue switch
                    {
                        bool => "bool",
                        string => "text",
                        _ => "number",
                    },
            };
            if (entry.Section is "Team Players" or "Team Lineups" && read.RawValue is long pointer
                && playersByAddress.TryGetValue(pointer, out RecordListItem? linked))
            {
                field["playerIndex"] = linked.Index;
                field["playerName"] = linked.Label;
            }
            bucket.Add(field);
        }
        var sections = new JsonArray();
        foreach (string name in order)
        {
            sections.Add(new JsonObject
            {
                ["name"] = name,
                ["fields"] = new JsonArray(buckets[name].Select(field => (JsonNode?)field).ToArray()),
            });
        }
        CheckTeamScope(session, scope);
        JsonObject result = TeamBranding(team, brandValues);
        result["index"] = team.Index;
        result["address"] = "0x" + team.Address.ToString("x", System.Globalization.CultureInfo.InvariantCulture);
        result["sections"] = sections;
        return result;
    }

    private sealed record TeamEdit(
        RecordEditScope Scope,
        Dictionary<string, FieldSpec> Specs,
        PlayerTransactionRecord Record,
        Dictionary<string, (FieldEntry Entry, FieldRead Value)> StableFields,
        List<PatchRow> Rows,
        string Path,
        JsonObject Journal);

    private readonly List<TeamEdit> _teamUndo = new();

    private static void CheckTeamScope(PlayerSession session, RecordEditScope scope)
    {
        session.Guard(scope.Epoch);
        scope.Check(session.Epoch!, session.Model.BaseAddressForKey("Team"));
    }

    private static void CheckTeamEdit(PlayerSession session, TeamEdit edit)
    {
        CheckTeamScope(session, edit.Scope);
        foreach (var pair in edit.StableFields.Values)
        {
            FieldRead current = session.Model.ReadField(pair.Entry, edit.Scope.Item);
            if (current.Address != pair.Value.Address
                || !PlayerPatchGuard.SameValue(current.RawValue, pair.Value.RawValue))
            {
                throw new InvalidOperationException("球队标识已变化，操作已停止");
            }
        }
    }

    private PlayerPatchGuard TeamPatchGuard(PlayerSession session, TeamEdit edit, bool undo = false) =>
        new(new[] { (edit.Scope.Item, edit.Record) },
            () => TeamItems(),
            _ => Array.Empty<byte>(),
            key => edit.Specs[key],
            (spec, item) => FieldIo.FieldAddress(session.Memory, item.Address, spec.Payload,
                session.Model.ParentPayload("Teams", spec.Payload)),
            undo)
        {
            Memory = session.Memory,
        };

    private static void SaveTeamJournal(TeamEdit edit)
    {
        string temporary = Path.ChangeExtension(edit.Path, ".tmp");
        File.WriteAllText(temporary, edit.Journal.ToJsonString(Program.JsonOptions) + "\n",
            new System.Text.UTF8Encoding(false));
        File.Move(temporary, edit.Path, overwrite: true);
    }

    /// <summary>
    /// Write team fields. Everything is planned through an overlay first, the byte patches are
    /// journaled to disk, and only then applied — with automatic rollback if any step disagrees.
    /// </summary>
    private JsonNode ApplyTeam(int index, JsonObject changes)
    {
        PlayerSession session = Session();
        List<RecordListItem> teams = TeamItems();
        if (changes.Count == 0)
        {
            throw new InvalidOperationException("没有需要写入的字段");
        }
        RecordListItem team = RecordEditScope.FindByIndex(teams, index);
        var scope = new RecordEditScope(session.Epoch!, _teamTable, session.Model.DomainStride("Teams"), team);
        CheckTeamScope(session, scope);
        var entries = new Dictionary<string, FieldEntry>(StringComparer.Ordinal);
        foreach (FieldEntry entry in session.Model.GroupedFields("Teams"))
        {
            if (HistorySession.IsIndependentTeamField(entry, session.Model.FieldVersionPayload(entry.Field)))
            {
                continue;
            }
            entries[$"{entry.Section}/{entry.Group}/{entry.NormalizedName}"] = entry;
        }

        var overlay = new Overlay(session.Memory);
        var planned = new JsonArray();
        var specs = new Dictionary<string, FieldSpec>(StringComparer.Ordinal);
        var record = new PlayerTransactionRecord
        {
            Item = team, Index = team.Index, Address = team.Address, Player = team.Label,
        };
        foreach (KeyValuePair<string, JsonNode?> pair in changes)
        {
            if (!entries.TryGetValue(pair.Key, out FieldEntry? entry))
            {
                throw new InvalidOperationException($"未知球队字段：{pair.Key}");
            }
            JsonObject payload = session.Model.FieldVersionPayload(entry.Field);
            if (IsTeamTextReference(entry) || !FieldIo.ImplementedPayload(payload) || payload["readonly"] is JsonValue readOnly
                && readOnly.GetValue<bool>())
            {
                throw new InvalidOperationException($"{pair.Key} 不可写");
            }
            long address = FieldIo.FieldAddress(
                session.Memory, team.Address, payload, session.Model.ParentPayload("Teams", payload));
            object? before = FieldIo.ReadAuthoredValue(session.Memory, address, payload);
            object? after = ToClr(pair.Value);
            if (after is null)
            {
                throw new InvalidOperationException($"{pair.Key} 缺少数值");
            }
            FieldIo.WriteAuthoredValue(overlay, address, payload, after);
            specs[pair.Key] = new FieldSpec(pair.Key, entry.Section, entry.Group, entry, payload);
            record.FieldAddresses[pair.Key] = address;
            record.Changes.Add(new ChangeRecord(pair.Key, before!, after, after,
                FieldIo.ReadAuthoredValue(overlay, address, payload)));
            planned.Add(new JsonObject
            {
                ["key"] = pair.Key,
                ["label"] = entry.DisplayName,
                ["address"] = "0x" + address.ToString("x", System.Globalization.CultureInfo.InvariantCulture),
                ["before"] = Convert.ToString(before, System.Globalization.CultureInfo.InvariantCulture),
                ["after"] = Convert.ToString(after, System.Globalization.CultureInfo.InvariantCulture),
            });
        }
        for (int changeIndex = 0; changeIndex < record.Changes.Count; changeIndex++)
        {
            ChangeRecord change = record.Changes[changeIndex];
            record.Changes[changeIndex] = change with
            {
                ExpectedRaw = FieldIo.ReadAuthoredValue(overlay,
                    record.FieldAddresses[change.Field], specs[change.Field].Payload),
            };
        }
        List<PatchRow> rows = overlay.Patches().ToList();
        if (rows.Count == 0)
        {
            return new JsonObject
            {
                ["patches"] = 0,
                ["fields"] = planned,
                ["note"] = "新值与当前值相同，未写入",
                ["undoDepth"] = _teamUndo.Count,
            };
        }

        string directory = Path.Combine(session.DataDirectory, "team-edits");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory,
            $"{DateTime.Now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture)}"
            + $"-{Guid.NewGuid().ToString("N")[..8]}.json");
        var byteRows = new JsonArray();
        foreach (PatchRow row in rows)
        {
            byteRows.Add(new JsonObject
            {
                ["address"] = "0x" + row.Address.ToString("x", System.Globalization.CultureInfo.InvariantCulture),
                ["before"] = row.Before,
                ["after"] = row.After,
            });
        }
        var journal = new JsonObject
        {
            ["format"] = "nba2k27-team-edit/1",
            ["status"] = "prepared",
            ["error"] = null,
            ["epoch"] = new JsonArray(scope.Epoch.ToJsonArray().Select(value => (JsonNode?)JsonValue.Create(value)).ToArray()),
            ["table"] = "0x" + scope.Table.ToString("x", System.Globalization.CultureInfo.InvariantCulture),
            ["time"] = DateTimeOffset.Now.ToString("yyyy-MM-ddTHH:mm:sszzz",
                System.Globalization.CultureInfo.InvariantCulture),
            ["team"] = team.Label,
            ["teamIndex"] = team.Index,
            ["address"] = "0x" + team.Address.ToString("x", System.Globalization.CultureInfo.InvariantCulture),
            ["fields"] = planned.DeepClone(),
            ["patches"] = rows.Count,
            ["bytes"] = byteRows,
        };
        var stableFields = new Dictionary<string, (FieldEntry, FieldRead)>(StringComparer.Ordinal);
        foreach (string name in new[] { "COMMONID", "BASETEAMID" })
        {
            FieldEntry? identity = session.Model.FieldByName("Teams", name);
            if (identity is not null && !specs.Values.Any(spec => spec.Name == name))
            {
                stableFields[name] = (identity, session.Model.ReadField(identity, team));
            }
        }
        record.PatchAddresses.AddRange(rows.Select(row => row.Address));
        var edit = new TeamEdit(scope, specs, record, stableFields, rows, path, journal);
        SaveTeamJournal(edit);
        try
        {
            PatchApplier.ApplyPatches(session.Memory, rows,
                () => CheckTeamEdit(session, edit), TeamPatchGuard(session, edit));
        }
        catch (Exception exc)
        {
            journal["status"] = exc.Message.Contains("rollback incomplete:", StringComparison.Ordinal)
                ? "needs_review" : "failed";
            journal["error"] = exc.Message;
            SaveTeamJournal(edit);
            throw;
        }
        journal["status"] = "applied";
        _teamUndo.Add(edit);
        string? warning = null;
        try
        {
            SaveTeamJournal(edit);
        }
        catch (IOException exc)
        {
            warning = "修改已生效，但日志状态更新失败：" + exc.Message;
        }
        return new JsonObject
        {
            ["team"] = team.Label,
            ["fields"] = planned,
            ["patches"] = rows.Count,
            ["journal"] = path,
            ["undoDepth"] = _teamUndo.Count,
            ["note"] = warning,
        };
    }

    private JsonNode UndoTeam()
    {
        if (_teamUndo.Count == 0)
        {
            throw new InvalidOperationException("没有可撤销的球队编辑");
        }
        PlayerSession session = Session();
        TeamEdit edit = _teamUndo[^1];
        if (edit.Journal["status"]?.GetValue<string>() == "needs_review")
        {
            throw new InvalidOperationException("上次操作未完整恢复，请先检查日志");
        }
        CheckTeamEdit(session, edit);
        foreach (ChangeRecord change in edit.Record.Changes)
        {
            FieldRead current = session.Model.ReadField(edit.Specs[change.Field].Entry, edit.Scope.Item);
            if (current.Address != edit.Record.FieldAddresses[change.Field]
                || !PlayerPatchGuard.SameValue(current.RawValue, change.ExpectedRaw))
            {
                throw new InvalidOperationException("球队字段已在游戏中变化，撤销已停止");
            }
        }
        var reversed = new List<PatchRow>();
        foreach (PatchRow row in edit.Rows)
        {
            byte current = session.Memory.ReadBytes(row.Address, 1)[0];
            if ((current & row.Mask) != (row.After & row.Mask))
            {
                throw new InvalidOperationException("球队字段已在游戏中变化，撤销已停止");
            }
            byte restored = (byte)((current & ~row.Mask) | (row.Before & row.Mask));
            reversed.Add(new PatchRow(row.Address, current, restored, row.Mask));
        }
        try
        {
            PatchApplier.ApplyPatches(session.Memory, reversed,
                () => CheckTeamEdit(session, edit), TeamPatchGuard(session, edit, undo: true));
        }
        catch (Exception exc)
        {
            if (exc.Message.Contains("rollback incomplete:", StringComparison.Ordinal))
            {
                edit.Journal["status"] = "needs_review";
                edit.Journal["error"] = exc.Message;
                SaveTeamJournal(edit);
            }
            throw;
        }
        _teamUndo.RemoveAt(_teamUndo.Count - 1);
        edit.Journal["status"] = "undone";
        string? warning = null;
        try
        {
            SaveTeamJournal(edit);
        }
        catch (IOException exc)
        {
            warning = "撤销已完成，但日志状态更新失败：" + exc.Message;
        }
        return new JsonObject
        {
            ["team"] = edit.Scope.Item.Label,
            ["restored"] = reversed.Count,
            ["journal"] = edit.Path,
            ["undoDepth"] = _teamUndo.Count,
            ["note"] = warning,
        };
    }

    private PlayerSession Session() =>        _session ?? throw new InvalidOperationException("尚未连接游戏，请先连接");

    private static object? ToClr(JsonNode? node)
    {
        if (node is null)
        {
            return null;
        }
        if (node is not JsonValue value)
        {
            throw new InvalidOperationException("字段值必须是标量");
        }
        return value.GetValueKind() switch
        {
            System.Text.Json.JsonValueKind.True => true,
            System.Text.Json.JsonValueKind.False => false,
            System.Text.Json.JsonValueKind.String => value.GetValue<string>(),
            System.Text.Json.JsonValueKind.Number => value.GetValue<double>(),
            _ => null,
        };
    }

    private static JsonNode? ToJson(object? raw) => raw switch
    {
        null => null,
        bool flag => JsonValue.Create(flag),
        string text => JsonValue.Create(text),
        int number => JsonValue.Create(number),
        long number => JsonValue.Create(number),
        double number => double.IsFinite(number) ? JsonValue.Create(number) : null,
        float number => float.IsFinite(number) ? JsonValue.Create(number) : null,
        _ => JsonValue.Create(Convert.ToString(raw, System.Globalization.CultureInfo.InvariantCulture)),
    };

    public void Dispose()
    {
        lock (_gate)
        {
            _playerReads.Clear();
            _session?.Close();
            _session = null;
            _gs?.Dispose();
            _gs = null;
            _gsSnapshot = null;
            _inMatch?.Dispose();
            _inMatch = null;
            _inMatchSnapshot = null;
            _matchConnection?.Dispose();
            _matchConnection = null;
            ClearMatchDraft();
            _mover = null;
            _teamOptions = null;
        }
    }
}
