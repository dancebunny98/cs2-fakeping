using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Events;
using System.Text.Json;

namespace FakePing;

[MinimumApiVersion(80)]
public class FakePing : BasePlugin
{
    public override string ModuleName => "Fake Ping";
    public override string ModuleVersion => "3.2.0";
    public override string ModuleAuthor => "Dancebunny98";

    private readonly Dictionary<CCSPlayerController, FakePingData> _fakePingData = new();
    private Dictionary<ulong, FakePingData> _savedFakePingData = new();
    private Dictionary<ulong, FakePingData> _configFakePingData = new();
    private CounterStrikeSharp.API.Modules.Timers.Timer? _updateTimer;

    private string PluginDirectory =>
        Path.Combine(Application.RootDirectory, "configs", "plugins", "FakePing");
    private string ConfigFile => Path.Combine(PluginDirectory, "FakePingConfig.json");
    private string DataFile => Path.Combine(PluginDirectory, "FakePingData.json");

    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public override void Load(bool hotReload)
    {
        Directory.CreateDirectory(PluginDirectory);

        LoadConfig();
        LoadData();

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

        RegisterEventHandler<EventPlayerConnectFull>(OnPlayerConnectFull);
        RegisterEventHandler<EventPlayerDisconnect>(OnPlayerDisconnect);

        RegisterListener<Listeners.OnTick>(OnTick);

        _updateTimer = AddTimer(1.0f, UpdateDynamicPings, TimerFlags.REPEAT);

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

    public override void Unload(bool hotReload)
    {
        _updateTimer?.Kill();
        _updateTimer = null;
        SaveData();
        _fakePingData.Clear();
    }

    [RequiresPermissions("@css/root")]
    [CommandHelper(minArgs: 2, usage: "<player> <ping> OR <player> <min-max> <interval>")]
    private void OnFakePingCommand(CCSPlayerController? caller, CommandInfo command)
    {
        var target = FindPlayer(command.GetArg(1));
        if (target == null)
        {
            command.ReplyToCommand($"{ChatColors.Red} Player not found.");
            return;
        }

        ulong steamId = target.SteamID;

        // Динамический режим
        if (command.ArgCount >= 4)
        {
            string rangeArg = command.GetArg(2);
            string intervalArg = command.GetArg(3);

            if (!int.TryParse(intervalArg, out int interval) || interval < 1)
            {
                command.ReplyToCommand($"{ChatColors.Red} Interval must be >= 1 second.");
                return;
            }

            string[] parts = rangeArg.Split('-');
            if (parts.Length != 2 ||
                !int.TryParse(parts[0], out int min) ||
                !int.TryParse(parts[1], out int max) ||
                min > max || min < 0 || max > 4095)
            {
                command.ReplyToCommand($"{ChatColors.Red} Invalid range. Use format: min-max (e.g. 10-50).");
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

        // Статический режим
        if (!int.TryParse(command.GetArg(2), out int ping) || ping < 0 || ping > 4095)
        {
            command.ReplyToCommand($"{ChatColors.Red} Ping must be 0-4095.");
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

        command.ReplyToCommand($"{ChatColors.Green} Static fake ping set to {ping} ms for {target.PlayerName}.");
    }

    [RequiresPermissions("@css/root")]
    [CommandHelper(minArgs: 1, usage: "<player>")]
    private void OnFakePingRemoveCommand(CCSPlayerController? caller, CommandInfo command)
    {
        var target = FindPlayer(command.GetArg(1));
        if (target == null)
        {
            command.ReplyToCommand($"{ChatColors.Red} Player not found.");
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

        command.ReplyToCommand($"{ChatColors.Green} Fake ping removed for {target.PlayerName}.");
    }

    private HookResult OnPlayerConnectFull(EventPlayerConnectFull @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player != null && player.IsValid && !player.IsBot)
            RestorePlayer(player);
        return HookResult.Continue;
    }

    private HookResult OnPlayerDisconnect(EventPlayerDisconnect @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player != null)
            _fakePingData.Remove(player);
        return HookResult.Continue;
    }

    private void RestorePlayer(CCSPlayerController player)
    {
        if (player == null || !player.IsValid || player.IsBot)
            return;

        ulong steamId = player.SteamID;
        FakePingData? data = null;

        if (_configFakePingData.TryGetValue(steamId, out var configData))
            data = CloneData(configData);
        else if (_savedFakePingData.TryGetValue(steamId, out var savedData))
            data = CloneData(savedData);

        if (data == null)
        {
            _fakePingData.Remove(player);
            return;
        }

        if (data.IsDynamic)
        {
            data.CurrentPing = Random.Shared.Next(data.MinPing, data.MaxPing + 1);
            data.NextUpdateTime = GetCurrentUnixTimeSeconds() + data.IntervalSeconds;
        }
        else
        {
            data.CurrentPing = data.StaticPing;
        }

        _fakePingData[player] = data;
        ApplyPing(player, data);
    }

    private CCSPlayerController? FindPlayer(string input)
    {
        input = input.Trim();
        var players = Utilities.GetPlayers();

        // SteamID64 (перебором)
        if (ulong.TryParse(input, out ulong steamId) && steamId > 0)
        {
            foreach (var player in players)
                if (player != null && player.IsValid && !player.IsBot && player.SteamID == steamId)
                    return player;
        }

        // #userid
        if (input.StartsWith("#") && int.TryParse(input.Substring(1), out int userId))
        {
            var player = Utilities.GetPlayerFromUserid(userId);
            if (player != null && player.IsValid && !player.IsBot)
                return player;
        }

        // Exact name
        foreach (var player in players)
            if (player != null && player.IsValid && !player.IsBot && 
                player.PlayerName.Equals(input, StringComparison.OrdinalIgnoreCase))
                return player;

        // Partial name
        foreach (var player in players)
            if (player != null && player.IsValid && !player.IsBot && 
                player.PlayerName.Contains(input, StringComparison.OrdinalIgnoreCase))
                return player;

        return null;
    }

    private void UpdateDynamicPings()
    {
        float currentTime = GetCurrentUnixTimeSeconds();

        foreach (var pair in _fakePingData.ToList())
        {
            var player = pair.Key;
            var data = pair.Value;

            if (player == null || !player.IsValid || player.IsBot)
            {
                if (player != null)
                    _fakePingData.Remove(player);
                continue;
            }

            if (!data.IsDynamic)
            {
                data.CurrentPing = data.StaticPing;
                continue;
            }

            if (currentTime >= data.NextUpdateTime)
            {
                data.CurrentPing = Random.Shared.Next(data.MinPing, data.MaxPing + 1);
                data.NextUpdateTime = currentTime + data.IntervalSeconds;
            }
        }
    }

    private void OnTick()
    {
        foreach (var pair in _fakePingData.ToList())
        {
            var player = pair.Key;
            var data = pair.Value;
            if (player == null || !player.IsValid || player.IsBot)
                continue;
            ApplyPing(player, data);
        }
    }

    private void ApplyPing(CCSPlayerController player, FakePingData data)
    {
        if (player == null || !player.IsValid || player.IsBot)
            return;

        int ping = Math.Clamp(data.CurrentPing, 0, 4095);
        player.Ping = (uint)ping;
    }

    // Загрузка / сохранение конфига и данных
    private void LoadConfig()
    {
        try
        {
            if (!File.Exists(ConfigFile))
            {
                _configFakePingData = new Dictionary<ulong, FakePingData>();
                SaveConfig();
                Console.WriteLine($"[FakePing] Created config: {ConfigFile}");
                return;
            }

            string json = File.ReadAllText(ConfigFile);
            var config = JsonSerializer.Deserialize<Dictionary<ulong, FakePingData>>(json, _jsonOptions);
            _configFakePingData = config ?? new Dictionary<ulong, FakePingData>();
            ValidateAndNormalize(_configFakePingData);
            Console.WriteLine($"[FakePing] Loaded {_configFakePingData.Count} permanent config entries.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FakePing] ERROR loading config: {ex}");
            _configFakePingData = new Dictionary<ulong, FakePingData>();
        }
    }

    private void SaveConfig()
    {
        try
        {
            Directory.CreateDirectory(PluginDirectory);
            string json = JsonSerializer.Serialize(_configFakePingData, _jsonOptions);
            File.WriteAllText(ConfigFile, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FakePing] ERROR saving config: {ex}");
        }
    }

    private void LoadData()
    {
        try
        {
            if (!File.Exists(DataFile))
            {
                _savedFakePingData = new Dictionary<ulong, FakePingData>();
                SaveData();
                Console.WriteLine($"[FakePing] Created data file: {DataFile}");
                return;
            }

            string json = File.ReadAllText(DataFile);
            var data = JsonSerializer.Deserialize<Dictionary<ulong, FakePingData>>(json, _jsonOptions);
            _savedFakePingData = data ?? new Dictionary<ulong, FakePingData>();
            ValidateAndNormalize(_savedFakePingData);
            Console.WriteLine($"[FakePing] Loaded {_savedFakePingData.Count} saved entries.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FakePing] ERROR loading data: {ex}");
            _savedFakePingData = new Dictionary<ulong, FakePingData>();
        }
    }

    private void SaveData()
    {
        try
        {
            Directory.CreateDirectory(PluginDirectory);
            string json = JsonSerializer.Serialize(_savedFakePingData, _jsonOptions);
            File.WriteAllText(DataFile, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FakePing] ERROR saving data: {ex}");
        }
    }

    private void ValidateAndNormalize(Dictionary<ulong, FakePingData> data)
    {
        foreach (var pair in data)
        {
            var item = pair.Value;
            if (item == null) continue;

            item.StaticPing = Math.Clamp(item.StaticPing, 0, 4095);
            item.MinPing = Math.Clamp(item.MinPing, 0, 4095);
            item.MaxPing = Math.Clamp(item.MaxPing, 0, 4095);

            if (item.MinPing > item.MaxPing)
                (item.MinPing, item.MaxPing) = (item.MaxPing, item.MinPing);

            if (item.IntervalSeconds < 1)
                item.IntervalSeconds = 1;

            if (!item.IsDynamic)
                item.CurrentPing = item.StaticPing;
            else
                item.CurrentPing = Math.Clamp(item.CurrentPing, item.MinPing, item.MaxPing);
        }
    }

    private FakePingData CloneData(FakePingData data) => new FakePingData
    {
        IsDynamic = data.IsDynamic,
        StaticPing = data.StaticPing,
        MinPing = data.MinPing,
        MaxPing = data.MaxPing,
        IntervalSeconds = data.IntervalSeconds,
        NextUpdateTime = data.NextUpdateTime,
        CurrentPing = data.CurrentPing
    };

    private float GetCurrentUnixTimeSeconds() => (float)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0f;
}

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
