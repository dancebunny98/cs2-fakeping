using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;

namespace FakePing;

[MinimumApiVersion(80)]
public class FakePing : BasePlugin
{
    public override string ModuleName => "Fake Ping";
    public override string ModuleVersion => "1.0.0";
    public override string ModuleAuthor => "Your Name";

    private Dictionary<CCSPlayerController, int> _fakePing = new();

    public override void Load(bool hotReload)
    {
        AddCommand("css_fakeping", "Set fake ping. Usage: !fakeping <player> <ping>", OnFakePingCommand);
        AddCommand("css_fakeping_remove", "Remove fake ping. Usage: !fakeping_remove <player>", OnFakePingRemoveCommand);

        RegisterEventHandler<EventPlayerConnectFull>(OnPlayerConnectFull);
        RegisterEventHandler<EventPlayerDisconnect>(OnPlayerDisconnect);

        // ★★★ ОСНОВНОЕ ИЗМЕНЕНИЕ: обновляем пинг каждый тик ★★★
        RegisterListener<Listeners.OnTick>(OnTick);
    }

    public override void Unload(bool hotReload)
    {
        _fakePing.Clear();
    }

    private void OnTick()
    {
        // Обновляем пинг для всех игроков каждый тик
        foreach (var player in Utilities.GetPlayers())
        {
            if (player != null && player.IsValid && !player.IsBot)
            {
                UpdatePlayerPing(player);
            }
        }
    }

    [RequiresPermissions("@css/root")]
    [CommandHelper(minArgs: 2, usage: "<player> <ping>")]
    private void OnFakePingCommand(CCSPlayerController? caller, CommandInfo command)
    {
        var target = FindPlayer(command.GetArg(1));
        if (target == null)
        {
            command.ReplyToCommand($" {ChatColors.Red} Player not found.");
            return;
        }

        if (!int.TryParse(command.GetArg(2), out int ping) || ping < 0 || ping > 4095)
        {
            command.ReplyToCommand($" {ChatColors.Red} Ping must be 0-4095.");
            return;
        }

        _fakePing[target] = ping;
        command.ReplyToCommand($" {ChatColors.Green} Fake ping set to {ping} ms for {target.PlayerName}");
    }

    [RequiresPermissions("@css/root")]
    [CommandHelper(minArgs: 1, usage: "<player>")]
    private void OnFakePingRemoveCommand(CCSPlayerController? caller, CommandInfo command)
    {
        var target = FindPlayer(command.GetArg(1));
        if (target == null)
        {
            command.ReplyToCommand($" {ChatColors.Red} Player not found.");
            return;
        }

        if (_fakePing.ContainsKey(target))
            _fakePing.Remove(target);

        command.ReplyToCommand($" {ChatColors.Green} Fake ping removed for {target.PlayerName}");
    }

    private CCSPlayerController? FindPlayer(string input)
    {
        if (input.StartsWith("#") && int.TryParse(input.Substring(1), out int userId))
        {
            var player = Utilities.GetPlayerFromUserid(userId);
            if (player != null && player.IsValid && !player.IsBot)
                return player;
        }

        var players = Utilities.GetPlayers();

        foreach (var p in players)
            if (p != null && p.IsValid && !p.IsBot && p.PlayerName.Equals(input, StringComparison.OrdinalIgnoreCase))
                return p;

        foreach (var p in players)
            if (p != null && p.IsValid && !p.IsBot && p.PlayerName.Contains(input, StringComparison.OrdinalIgnoreCase))
                return p;

        return null;
    }

    private void UpdatePlayerPing(CCSPlayerController player)
    {
        if (player == null || !player.IsValid || player.IsBot) return;

        if (_fakePing.TryGetValue(player, out int fakePing))
        {
            // Множественная запись для надёжности
            player.Ping = (uint)fakePing;
            player.Ping = (uint)fakePing;
        }
        // Если фейк отключен – ничего не делаем, оставляем реальный пинг
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
