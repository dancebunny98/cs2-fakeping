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
        AddCommand("css_fakeping", "Set fake ping. Usage: !fakeping <player> <ping>", OnFakePingCommand);
        AddCommand("css_fakeping_remove", "Remove fake ping. Usage: !fakeping_remove <player>", OnFakePingRemoveCommand);

        RegisterEventHandler<EventPlayerConnectFull>(OnPlayerConnectFull);
        RegisterEventHandler<EventPlayerDisconnect>(OnPlayerDisconnect);

        // Обновляем пинг каждые 0.5 секунды для мгновенного отображения
        _updateTimer = AddTimer(0.5f, UpdateAllPings, TimerFlags.REPEAT);
    }

    public override void Unload(bool hotReload)
    {
        _updateTimer?.Kill();
        _fakePing.Clear();
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
        UpdatePlayerPing(target);
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

        UpdatePlayerPing(target);
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

    // ★★★ Используем схему для прямого доступа к m_iPing ★★★
    private void UpdatePlayerPing(CCSPlayerController player)
    {
        if (player == null || !player.IsValid || player.IsBot) return;

        // Получаем доступ к схеме игрока
        var schema = player.As<PlayerSchema>();

        if (_fakePing.TryGetValue(player, out int fakePing))
        {
            // Устанавливаем фейковый пинг напрямую в сетевую переменную
            schema.m_iPing = (uint)fakePing;
        }
        else
        {
            // Сбрасываем на 0 – сервер сам подставит реальный пинг (если он есть)
            schema.m_iPing = 0;
        }
    }

    private void UpdateAllPings()
    {
        foreach (var player in Utilities.GetPlayers())
        {
            if (player != null && player.IsValid && !player.IsBot)
                UpdatePlayerPing(player);
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

// ★★★ КЛАСС ДЛЯ РАБОТЫ СО СХЕМОЙ ★★★
public class PlayerSchema : BaseSchema
{
    [SchemaMember("CCSPlayerController", "m_iPing")]
    public ref uint m_iPing => ref Schema.GetRef<uint>(this.Handle, "CCSPlayerController", "m_iPing");
}
