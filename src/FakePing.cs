using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Modules.Timers;
using System.Text.Json;

namespace FakePing;

[MinimumApiVersion(80)]
public class FakePing : BasePlugin
{
    public override string ModuleName => "Fake Ping";
    public override string ModuleVersion => "3.1.0";
    public override string ModuleAuthor => "Your Name";

    // =========================================================
    // RUNTIME DATA
    // =========================================================

    private readonly Dictionary<CCSPlayerController, FakePingData> _fakePingData = new();

    // Настройки, введённые через команды.
    // SteamID64 -> FakePingData
    private Dictionary<ulong, FakePingData> _savedFakePingData = new();

    // Настройки из ручного конфига.
    // SteamID64 -> FakePingData
    private Dictionary<ulong, FakePingData> _configFakePingData = new();

    private CounterStrikeSharp.API.Modules.Timers.Timer? _updateTimer;

    // =========================================================
    // FILE PATHS
    // =========================================================

    private string PluginDirectory =>
        Path.Combine(
            Server.GameDirectory,
            "addons",
            "counterstrikesharp",
            "configs",
            "plugins",
            "FakePing"
        );

    private string ConfigFile =>
        Path.Combine(
            PluginDirectory,
            "FakePingConfig.json"
        );

    private string DataFile =>
        Path.Combine(
            PluginDirectory,
            "FakePingData.json"
        );

    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    // =========================================================
    // LOAD
    // =========================================================

    public override void Load(bool hotReload)
    {
        Directory.CreateDirectory(PluginDirectory);

        LoadConfig();
        LoadData();

        // -----------------------------------------------------
        // COMMANDS
        // -----------------------------------------------------

        AddCommand(
            "css_fakeping",
            "Set fake ping. Usage: !fakeping <player> <ping> OR !fakeping <player> <min-max> <interval>",
            OnFakePingCommand
        );

        AddCommand(
            "css_fakeping_remove",
            "Remove fake ping. Usage: !fakeping_remove <player>",
            OnFakePingRemoveCommand
        );

        // -----------------------------------------------------
        // EVENTS
        // -----------------------------------------------------

        RegisterEventHandler<EventPlayerChat>(
            OnPlayerChat,
            HookMode.Pre
        );

        RegisterEventHandler<EventPlayerConnectFull>(
            OnPlayerConnectFull
        );

        RegisterEventHandler<EventPlayerDisconnect>(
            OnPlayerDisconnect
        );

        // -----------------------------------------------------
        // TICK
        // -----------------------------------------------------

        RegisterListener<Listeners.OnTick>(OnTick);

        // -----------------------------------------------------
        // DYNAMIC PING TIMER
        // -----------------------------------------------------

        _updateTimer = AddTimer(
            1.0f,
            UpdateDynamicPings,
            TimerFlags.REPEAT
        );

        // -----------------------------------------------------
        // HOT RELOAD
        // -----------------------------------------------------

        if (hotReload)
        {
            foreach (var player in Utilities.GetPlayers())
            {
                if (player == null || !player.IsValid || player.IsBot)
                    continue;

                RestorePlayer(player);
            }
        }

        Console.WriteLine(
            $"[FakePing] Loaded. Config entries: {_configFakePingData.Count}, saved entries: {_savedFakePingData.Count}"
        );
    }

    // =========================================================
    // UNLOAD
    // =========================================================

    public override void Unload(bool hotReload)
    {
        _updateTimer?.Kill();
        _updateTimer = null;

        SaveData();

        _fakePingData.Clear();
    }

    // =========================================================
    // HIDE CHAT COMMANDS
    // =========================================================

    private HookResult OnPlayerChat(
        EventPlayerChat @event,
        GameEventInfo info
    )
    {
        string text = @event.Text?.Trim() ?? string.Empty;

        if (string.IsNullOrEmpty(text))
            return HookResult.Continue;

        text = text.TrimStart();

        if (
            IsChatCommand(text, "!fakeping") ||
            IsChatCommand(text, "/fakeping") ||
            IsChatCommand(text, "!fakeping_remove") ||
            IsChatCommand(text, "/fakeping_remove")
        )
        {
            info.DontBroadcast = true;
        }

        return HookResult.Continue;
    }

    private bool IsChatCommand(string text, string command)
    {
        if (!text.StartsWith(command, StringComparison.OrdinalIgnoreCase))
            return false;

        if (text.Length == command.Length)
            return true;

        char next = text[command.Length];

        return char.IsWhiteSpace(next);
    }

    // =========================================================
    // FAKEPING COMMAND
    // =========================================================

    [RequiresPermissions("@css/root")]
    [CommandHelper(
        minArgs: 2,
        usage: "<player> <ping> OR <player> <min-max> <interval>"
    )]
    private void OnFakePingCommand(
        CCSPlayerController? caller,
        CommandInfo command
    )
    {
        var target = FindPlayer(command.GetArg(1));

        if (target == null)
        {
            command.ReplyToCommand(
                $"{ChatColors.Red} Player not found."
            );

            return;
        }

        ulong steamId = target.SteamID;

        // =====================================================
        // DYNAMIC MODE
        // =====================================================

        if (command.ArgCount >= 4)
        {
            string rangeArg = command.GetArg(2);
            string intervalArg = command.GetArg(3);

            if (
                !int.TryParse(
                    intervalArg,
                    out int interval
                ) ||
                interval < 1
            )
            {
                command.ReplyToCommand(
                    $"{ChatColors.Red} Interval must be >= 1 second."
                );

                return;
            }

            string[] parts = rangeArg.Split('-');

            if (
                parts.Length != 2 ||
                !int.TryParse(parts[0], out int min) ||
                !int.TryParse(parts[1], out int max) ||
                min > max ||
                min < 0 ||
                max > 4095
            )
            {
                command.ReplyToCommand(
                    $"{ChatColors.Red} Invalid range. Use format: min-max (e.g. 10-50)."
                );

                return;
            }

            var data = new FakePingData
            {
                IsDynamic = true,
                StaticPing = 0,
                MinPing = min,
                MaxPing = max,
                IntervalSeconds = interval,
                NextUpdateTime = 0,
                CurrentPing = min
            };

            _fakePingData[target] = CloneData(data);
            _savedFakePingData[steamId] = CloneData(data);

            SaveData();

            command.ReplyToCommand(
                $"{ChatColors.Green} Dynamic fake ping enabled for {target.PlayerName}: range {min}-{max} ms, change every {interval} sec."
            );

            return;
        }

        // =====================================================
        // STATIC MODE
        // =====================================================

        if (
            !int.TryParse(
                command.GetArg(2),
                out int ping
            ) ||
            ping < 0 ||
            ping > 4095
        )
        {
            command.ReplyToCommand(
                $"{ChatColors.Red} Ping must be 0-4095."
            );

            return;
        }

        var staticData = new FakePingData
        {
            IsDynamic = false,
            StaticPing = ping,
            MinPing = 0,
            MaxPing = 0,
            IntervalSeconds = 0,
            NextUpdateTime = 0,
            CurrentPing = ping
        };

        _fakePingData[target] = CloneData(staticData);
        _savedFakePingData[steamId] = CloneData(staticData);

        SaveData();

        command.ReplyToCommand(
            $"{ChatColors.Green} Static fake ping set to {ping} ms for {target.PlayerName}."
        );
    }

    // =========================================================
    // REMOVE COMMAND
    // =========================================================

    [RequiresPermissions("@css/root")]
    [CommandHelper(
        minArgs: 1,
        usage: "<player>"
    )]
    private void OnFakePingRemoveCommand(
        CCSPlayerController? caller,
        CommandInfo command
    )
    {
        var target = FindPlayer(command.GetArg(1));

        if (target == null)
        {
            command.ReplyToCommand(
                $"{ChatColors.Red} Player not found."
            );

            return;
        }

        ulong steamId = target.SteamID;

        if (_configFakePingData.ContainsKey(steamId))
        {
            command.ReplyToCommand(
                $"{ChatColors.Red} This player has a permanent fake ping in FakePingConfig.json. Remove the SteamID from the config first."
            );

            return;
        }

        _fakePingData.Remove(target);
        _savedFakePingData.Remove(steamId);

        SaveData();

        command.ReplyToCommand(
            $"{ChatColors.Green} Fake ping removed for {target.PlayerName}."
        );
    }

    // =========================================================
    // PLAYER CONNECT
    // =========================================================

    private HookResult OnPlayerConnectFull(
        EventPlayerConnectFull @event,
        GameEventInfo info
    )
    {
        var player = @event.Userid;

        if (
            player == null ||
            !player.IsValid ||
            player.IsBot
        )
        {
            return HookResult.Continue;
        }

        RestorePlayer(player);

        return HookResult.Continue;
    }

    // =========================================================
    // PLAYER DISCONNECT
    // =========================================================

    private HookResult OnPlayerDisconnect(
        EventPlayerDisconnect @event,
        GameEventInfo info
    )
    {
        var player = @event.Userid;

        if (player != null)
            _fakePingData.Remove(player);

        return HookResult.Continue;
    }

    // =========================================================
    // RESTORE PLAYER
    // =========================================================

    private void RestorePlayer(
        CCSPlayerController player
    )
    {
        if (
            player == null ||
            !player.IsValid ||
            player.IsBot
        )
        {
            return;
        }

        ulong steamId = player.SteamID;

        FakePingData? data = null;

        if (
            _configFakePingData.TryGetValue(
                steamId,
                out var configData
            )
        )
        {
            data = CloneData(configData);
        }

        else if (
            _savedFakePingData.TryGetValue(
                steamId,
                out var savedData
            )
        )
        {
            data = CloneData(savedData);
        }

        if (data == null)
        {
            _fakePingData.Remove(player);
            return;
        }

        if (data.IsDynamic)
        {
            data.CurrentPing = Random.Shared.Next(
                data.MinPing,
                data.MaxPing + 1
            );

            data.NextUpdateTime =
                GetCurrentUnixTimeSeconds() +
                data.IntervalSeconds;
        }
        else
        {
            data.CurrentPing = data.StaticPing;
        }

        _fakePingData[player] = data;

        ApplyPing(
            player,
            data
        );
    }

    // =========================================================
    // FIND PLAYER
    // =========================================================

    private CCSPlayerController? FindPlayer(
        string input
    )
    {
        input = input.Trim();

        var players = Utilities.GetPlayers();

        // =====================================================
        // STEAMID64 (перебором)
        // =====================================================
        if (ulong.TryParse(input, out ulong steamId) && steamId > 0)
        {
            foreach (var player in players)
            {
                if (player != null && player.IsValid && !player.IsBot && player.SteamID == steamId)
                    return player;
            }
        }

        // =====================================================
        // #USERID
        // =====================================================

        if (
            input.StartsWith("#") &&
            int.TryParse(
                input.Substring(1),
                out int userId
            )
        )
        {
            var player =
                Utilities.GetPlayerFromUserid(
                    userId
                );

            if (
                player != null &&
                player.IsValid &&
                !player.IsBot
            )
            {
                return player;
            }
        }

        // =====================================================
        // EXACT NAME
        // =====================================================

        foreach (var player in players)
        {
            if (
                player != null &&
                player.IsValid &&
                !player.IsBot &&
                player.PlayerName.Equals(
                    input,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return player;
            }
        }

        // =====================================================
        // PARTIAL NAME
        // =====================================================

        foreach (var player in players)
        {
            if (
                player != null &&
                player.IsValid &&
                !player.IsBot &&
                player.PlayerName.Contains(
                    input,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return player;
            }
        }

        return null;
    }

    // =========================================================
    // UPDATE DYNAMIC PINGS
    // =========================================================

    private void UpdateDynamicPings()
    {
        float currentTime =
            GetCurrentUnixTimeSeconds();

        foreach (var pair in _fakePingData.ToList())
        {
            var player = pair.Key;
            var data = pair.Value;

            if (
                player == null ||
                !player.IsValid ||
                player.IsBot
            )
            {
                if (player != null)
                    _fakePingData.Remove(player);

                continue;
            }

            if (!data.IsDynamic)
            {
                data.CurrentPing =
                    data.StaticPing;

                continue;
            }

            if (currentTime >= data.NextUpdateTime)
            {
                data.CurrentPing =
                    Random.Shared.Next(
                        data.MinPing,
                        data.MaxPing + 1
                    );

                data.NextUpdateTime =
                    currentTime +
                    data.IntervalSeconds;
            }
        }
    }

    // =========================================================
    // ON TICK
    // =========================================================

    private void OnTick()
    {
        foreach (var pair in _fakePingData.ToList())
        {
            var player = pair.Key;
            var data = pair.Value;

            if (
                player == null ||
                !player.IsValid ||
                player.IsBot
            )
            {
                continue;
            }

            ApplyPing(
                player,
                data
            );
        }
    }

    // =========================================================
    // APPLY PING
    // =========================================================

    private void ApplyPing(
        CCSPlayerController player,
        FakePingData data
    )
    {
        if (
            player == null ||
            !player.IsValid ||
            player.IsBot
        )
        {
            return;
        }

        int ping = Math.Clamp(
            data.CurrentPing,
            0,
            4095
        );

        player.Ping = (uint)ping;
    }

    // =========================================================
    // LOAD / SAVE CONFIG
    // =========================================================

    private void LoadConfig()
    {
        try
        {
            if (!File.Exists(ConfigFile))
            {
                _configFakePingData =
                    new Dictionary<ulong, FakePingData>();

                SaveConfig();

                Console.WriteLine(
                    $"[FakePing] Created config: {ConfigFile}"
                );

                return;
            }

            string json =
                File.ReadAllText(ConfigFile);

            var config =
                JsonSerializer.Deserialize<
                    Dictionary<ulong, FakePingData>
                >(
                    json,
                    _jsonOptions
                );

            _configFakePingData =
                config ??
                new Dictionary<ulong, FakePingData>();

            ValidateAndNormalize(
                _configFakePingData
            );

            Console.WriteLine(
                $"[FakePing] Loaded {_configFakePingData.Count} permanent config entries."
            );
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"[FakePing] ERROR loading config: {ex}"
            );

            _configFakePingData =
                new Dictionary<ulong, FakePingData>();
        }
    }

    private void SaveConfig()
    {
        try
        {
            Directory.CreateDirectory(
                PluginDirectory
            );

            string json =
                JsonSerializer.Serialize(
                    _configFakePingData,
                    _jsonOptions
                );

            File.WriteAllText(
                ConfigFile,
                json
            );
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"[FakePing] ERROR saving config: {ex}"
            );
        }
    }

    // =========================================================
    // LOAD / SAVE DATA
    // =========================================================

    private void LoadData()
    {
        try
        {
            if (!File.Exists(DataFile))
            {
                _savedFakePingData =
                    new Dictionary<ulong, FakePingData>();

                SaveData();

                Console.WriteLine(
                    $"[FakePing] Created data file: {DataFile}"
                );

                return;
            }

            string json =
                File.ReadAllText(DataFile);

            var data =
                JsonSerializer.Deserialize<
                    Dictionary<ulong, FakePingData>
                >(
                    json,
                    _jsonOptions
                );

            _savedFakePingData =
                data ??
                new Dictionary<ulong, FakePingData>();

            ValidateAndNormalize(
                _savedFakePingData
            );

            Console.WriteLine(
                $"[FakePing] Loaded {_savedFakePingData.Count} saved entries."
            );
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"[FakePing] ERROR loading data: {ex}"
            );

            _savedFakePingData =
                new Dictionary<ulong, FakePingData>();
        }
    }

    private void SaveData()
    {
        try
        {
            Directory.CreateDirectory(
                PluginDirectory
            );

            string json =
                JsonSerializer.Serialize(
                    _savedFakePingData,
                    _jsonOptions
                );

            File.WriteAllText(
                DataFile,
                json
            );
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                $"[FakePing] ERROR saving data: {ex}"
            );
        }
    }

    // =========================================================
    // VALIDATE / NORMALIZE
    // =========================================================

    private void ValidateAndNormalize(
        Dictionary<ulong, FakePingData> data
    )
    {
        foreach (var pair in data)
        {
            var item = pair.Value;

            if (item == null)
                continue;

            item.StaticPing =
                Math.Clamp(
                    item.StaticPing,
                    0,
                    4095
                );

            item.MinPing =
                Math.Clamp(
                    item.MinPing,
                    0,
                    4095
                );

            item.MaxPing =
                Math.Clamp(
                    item.MaxPing,
                    0,
                    4095
                );

            if (item.MinPing > item.MaxPing)
            {
                int temp =
                    item.MinPing;

                item.MinPing =
                    item.MaxPing;

                item.MaxPing =
                    temp;
            }

            if (item.IntervalSeconds < 1)
                item.IntervalSeconds = 1;

            if (!item.IsDynamic)
            {
                item.CurrentPing =
                    item.StaticPing;
            }
            else
            {
                item.CurrentPing =
                    Math.Clamp(
                        item.CurrentPing,
                        item.MinPing,
                        item.MaxPing
                    );
            }
        }
    }

    // =========================================================
    // CLONE
    // =========================================================

    private FakePingData CloneData(
        FakePingData data
    )
    {
        return new FakePingData
        {
            IsDynamic =
                data.IsDynamic,

            StaticPing =
                data.StaticPing,

            MinPing =
                data.MinPing,

            MaxPing =
                data.MaxPing,

            IntervalSeconds =
                data.IntervalSeconds,

            NextUpdateTime =
                data.NextUpdateTime,

            CurrentPing =
                data.CurrentPing
        };
    }

    // =========================================================
    // UNIX TIME
    // =========================================================

    private float GetCurrentUnixTimeSeconds()
    {
        return (float)
            DateTimeOffset.UtcNow
                .ToUnixTimeMilliseconds()
            / 1000.0f;
    }
}

// =============================================================
// DATA MODEL
// =============================================================

public class FakePingData
{
    public bool IsDynamic { get; set; }

    public int StaticPing { get; set; }

    public int MinPing { get; set; }

    public int MaxPing { get; set; }

    public int IntervalSeconds { get; set; }

    public float NextUpdateTime { get; set; }

    public int CurrentPing { get; set; }
}
