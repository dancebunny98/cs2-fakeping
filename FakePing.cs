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
    private Timer? _updateTimer;

    public override void Load(bool hotReload)
    {
        // Регистрация команд
        AddCommand("css_fakeping", "Set fake ping for a player. Usage: !fakeping <#userid or name> <ping>", OnFakePingCommand, CommandUsage.CLIENT_AND_SERVER);
        AddCommand("css_fakeping_remove", "Remove fake ping from a player. Usage: !fakeping_remove <#userid or name>", OnFakePingRemoveCommand, CommandUsage.CLIENT_AND_SERVER);

        // Запускаем таймер для обновления пинга каждые 5 секунд
        _updateTimer = AddTimer(5.0f, UpdateAllPings, TimerFlags.REPEAT);
    }

    public override void Unload(bool hotReload)
    {
        _updateTimer?.Kill();
        _fakePing.Clear();
    }

    [RequiresPermissions("@css/root")] // Можно заменить на @css/slay
    [CommandHelper(minArgs: 2, usage: "<#userid or name> <ping>", who: CommandUsage.CLIENT_AND_SERVER)]
    private void OnFakePingCommand(CCSPlayerController? caller, CommandInfo commandInfo)
    {
        string targetName = commandInfo.GetArg(1);
        var targets = CommandHelpers.GetPlayersFromTarget(targetName, out _);
        
        if (targets.Count == 0)
        {
            commandInfo.ReplyToCommand($" {ChatColors.Red} Player '{targetName}' not found.");
            return;
        }

        if (targets.Count > 1)
        {
            commandInfo.ReplyToCommand($" {ChatColors.Red} Multiple players match '{targetName}'. Please be more specific.");
            return;
        }

        var target = targets.First();

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
    [CommandHelper(minArgs: 1, usage: "<#userid or name>", who: CommandUsage.CLIENT_AND_SERVER)]
    private void OnFakePingRemoveCommand(CCSPlayerController? caller, CommandInfo commandInfo)
    {
        string targetName = commandInfo.GetArg(1);
        var targets = CommandHelpers.GetPlayersFromTarget(targetName, out _);

        if (targets.Count == 0)
        {
            commandInfo.ReplyToCommand($" {ChatColors.Red} Player '{targetName}' not found.");
            return;
        }

        if (targets.Count > 1)
        {
            commandInfo.ReplyToCommand($" {ChatColors.Red} Multiple players match '{targetName}'. Please be more specific.");
            return;
        }

        var target = targets.First();

        if (_fakePing.ContainsKey(target))
            _fakePing.Remove(target);

        // Сбрасываем пинг, установив его в 0 (или можно оставить реальный)
        target.Ping = 0;
        commandInfo.ReplyToCommand($" {ChatColors.Green} Fake ping removed for {target.PlayerName}");
    }

    private void UpdatePlayerPing(CCSPlayerController player)
    {
        if (player == null || !player.IsValid || player.IsBot) return;

        if (_fakePing.TryGetValue(player, out int fakePing))
        {
            player.Ping = (uint)fakePing;
        }
        // Если нет записи, ничего не делаем, чтобы не сбивать реальный пинг
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

    public override void OnPlayerConnect(CCSPlayerController player)
    {
        if (player != null && _fakePing.ContainsKey(player))
            _fakePing.Remove(player);
    }

    public override void OnPlayerDisconnect(CCSPlayerController player)
    {
        if (player != null && _fakePing.ContainsKey(player))
            _fakePing.Remove(player);
    }
}
