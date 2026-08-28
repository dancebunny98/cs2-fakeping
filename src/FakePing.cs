using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Modules.Timers;

namespace FakePing;

[MinimumApiVersion(80)]
public class FakePing : BasePlugin
{
    public override string ModuleName => "Fake Ping";
    public override string ModuleVersion => "1.0.0";
    public override string ModuleAuthor => "Your Name";

    private Dictionary<CCSPlayerController, int> _fakePing = new();
    private CounterStrikeSharp.API.Modules.Timers.Timer? _updateTimer;

    public override void Load(bool hotReload)
    {
        AddCommand("css_fakeping", "Set fake ping for a player. Usage: !fakeping <#userid or name> <ping>", OnFakePingCommand);
        AddCommand("css_fakeping_remove", "Remove fake ping from a player. Usage: !fakeping_remove <#userid or name>", OnFakePingRemoveCommand);

        RegisterEventHandler<EventPlayerConnectFull>(OnPlayerConnectFull);
        RegisterEventHandler<EventPlayerDisconnect>(OnPlayerDisconnect);

        _updateTimer = AddTimer(5.0f, UpdateAllPings, TimerFlags.REPEAT);
    }

    public override void Unload(bool hotReload)
    {
        _updateTimer?.Kill();
        _fakePing.Clear();
    }

    [RequiresPermissions("@css/root")]
    [CommandHelper(minArgs: 2, usage: "<#userid or name> <ping>")]
    private void OnFakePingCommand(CCSPlayerController? caller, CommandInfo commandInfo)
    {
        string targetName = commandInfo.GetArg(1);
        var target = FindPlayer(targetName);
        
        if (target == null)
        {
            commandInfo.ReplyToCommand($" {ChatColors.Red} Player '{targetName}' not found.");
            return;
        }

        if (!int.TryParse(commandInfo.GetArg(2), out int ping) || ping < 0 || ping > 4095)
        {
            commandInfo.ReplyToCommand($" {ChatColors.Red} Invalid ping value. Must be between 0 and 4095.");
            return;
        }

        _fakePing[target] = ping;
        UpdatePlayerPing(target);

        commandInfo.ReplyToCommand($" {ChatColors.Green} Fake ping set to {ping} ms for {target.PlayerName}");
    }

    [RequiresPermissions("@css/root")]
    [CommandHelper(minArgs: 1, usage: "<#userid or name>")]
    private void OnFakePingRemoveCommand(CCSPlayerController? caller, CommandInfo commandInfo)
    {
        string targetName = commandInfo.GetArg(1);
        var target = FindPlayer(targetName);

        if (target == null)
        {
            commandInfo.ReplyToCommand($" {ChatColors.Red} Player '{targetName}' not found.");
            return;
        }

        if (_fakePing.ContainsKey(target))
            _fakePing.Remove(target);

        target.Ping = 0;
        commandInfo.ReplyToCommand($" {ChatColors.Green} Fake ping removed for {target.PlayerName}");
    }

    // Поиск игрока по строке: поддерживает #userid или частичное имя
    private CCSPlayerController? FindPlayer(string input)
    {
        var players = Utilities.GetPlayers();
        CCSPlayerController? result = null;

        // Проверяем, не указан ли userid (#123)
        if (input.StartsWith("#") && int.TryParse(input.Substring(1), out int userId))
        {
            foreach (var player in players)
            {
                if (player != null && player.UserId == userId)
                    return player;
            }
            return null;
        }

        // Ищем по точному совпадению имени (без учета регистра)
        foreach (var player in players)
        {
            if (player != null && player.PlayerName != null && 
                player.PlayerName.Equals(input, StringComparison.OrdinalIgnoreCase))
            {
                return player;
            }
        }

        // Ищем по частичному совпадению (содержит подстроку)
        foreach (var player in players)
        {
            if (player != null && player.PlayerName != null && 
                player.PlayerName.Contains(input, StringComparison.OrdinalIgnoreCase))
            {
                // Если уже найден один, то возвращаем null (неоднозначность)
                if (result != null)
                    return null;
                result = player;
            }
        }

        return result;
    }

    private void UpdatePlayerPing(CCSPlayerController player)
    {
        if (player == null || !player.IsValid || player.IsBot) return;

        if (_fakePing.TryGetValue(player, out int fakePing))
        {
            player.Ping = (uint)fakePing;
        }
    }

    private void UpdateAllPings()
    {
        foreach (var player in Utilities.GetPlayers())
        {
            if (player != null && player.IsValid && !player.IsBot)
            {
                UpdatePlayerPing(player);
            }
        }
    }

    private HookResult OnPlayerConnectFull(EventPlayerConnectFull @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player != null && _fakePing.ContainsKey(player))
            _fakePing.Remove(player);
        return HookResult.Continue;
    }

    private HookResult OnPlayerDisconnect(EventPlayerDisconnect @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player != null && _fakePing.ContainsKey(player))
            _fakePing.Remove(player);
        return HookResult.Continue;
    }
}
