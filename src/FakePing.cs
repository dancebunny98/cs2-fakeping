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

    // Словарь для хранения фейкового пинга каждого игрока
    private Dictionary<CCSPlayerController, int> _fakePing = new();
    private CounterStrikeSharp.API.Modules.Timers.Timer? _updateTimer;

    public override void Load(bool hotReload)
    {
        // Регистрируем команды
        AddCommand("css_fakeping", "Set fake ping for a player. Usage: !fakeping <#userid or name> <ping>", OnFakePingCommand);
        AddCommand("css_fakeping_remove", "Remove fake ping from a player. Usage: !fakeping_remove <#userid or name>", OnFakePingRemoveCommand);

        // Регистрируем события для очистки данных
        RegisterEventHandler<EventPlayerConnectFull>(OnPlayerConnectFull);
        RegisterEventHandler<EventPlayerDisconnect>(OnPlayerDisconnect);

        // Запускаем таймер для периодического обновления пинга (каждые 3 секунды)
        _updateTimer = AddTimer(3.0f, UpdateAllPings, TimerFlags.REPEAT);
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
        var target = FindPlayer(commandInfo.GetArg(1));
        if (target == null)
        {
            commandInfo.ReplyToCommand($" {ChatColors.Red} Player not found.");
            return;
        }

        if (!int.TryParse(commandInfo.GetArg(2), out int ping) || ping < 0 || ping > 4095)
        {
            commandInfo.ReplyToCommand($" {ChatColors.Red} Invalid ping (0-4095).");
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
        var target = FindPlayer(commandInfo.GetArg(1));
        if (target == null)
        {
            commandInfo.ReplyToCommand($" {ChatColors.Red} Player not found.");
            return;
        }

        if (_fakePing.ContainsKey(target))
            _fakePing.Remove(target);

        // Сбрасываем пинг, устанавливая его в 0
        UpdatePlayerPing(target);
        commandInfo.ReplyToCommand($" {ChatColors.Green} Fake ping removed for {target.PlayerName}");
    }

    private CCSPlayerController? FindPlayer(string input)
    {
        // Поиск по #userid
        if (input.StartsWith("#") && int.TryParse(input.Substring(1), out int userId))
        {
            var player = Utilities.GetPlayerFromUserid(userId);
            if (player != null && player.IsValid && !player.IsBot)
                return player;
        }

        var players = Utilities.GetPlayers();

        // Точное совпадение по имени
        foreach (var p in players)
        {
            if (p != null && p.IsValid && !p.IsBot && p.PlayerName.Equals(input, StringComparison.OrdinalIgnoreCase))
                return p;
        }

        // Частичное совпадение (первый подходящий)
        foreach (var p in players)
        {
            if (p != null && p.IsValid && !p.IsBot && p.PlayerName.Contains(input, StringComparison.OrdinalIgnoreCase))
                return p;
        }

        return null;
    }

    // ★★★ ГЛАВНОЕ ИЗМЕНЕНИЕ: используем SchemaMember для прямого доступа к m_iPing ★★★
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
            // Если фейк-пинг отключен, ставим 0, чтобы сервер показывал реальное значение
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

    // Обработчик события подключения игрока
    private HookResult OnPlayerConnectFull(EventPlayerConnectFull @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player != null && _fakePing.ContainsKey(player))
            _fakePing.Remove(player);
        return HookResult.Continue;
    }

    // Обработчик события отключения игрока
    private HookResult OnPlayerDisconnect(EventPlayerDisconnect @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player != null && _fakePing.ContainsKey(player))
            _fakePing.Remove(player);
        return HookResult.Continue;
    }
}

// ★★★ НОВЫЙ КЛАСС ДЛЯ ДОСТУПА К СХЕМЕ ИГРОКА ★★★
public class PlayerSchema : BaseSchema
{
    [SchemaMember("CCSPlayerController", "m_iPing")]
    public ref uint m_iPing => ref Schema.GetRef<uint>(this.Handle, "CCSPlayerController", "m_iPing");
}
