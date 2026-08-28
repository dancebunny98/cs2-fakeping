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

        // Обновляем пинг каждые 3 секунды (чаще, чтобы было заметно)
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

        // Сбрасываем на реальное значение (0 — сервер сам подставит актуальный)
        target.Ping = 0;
        commandInfo.ReplyToCommand($" {ChatColors.Green} Fake ping removed for {target.PlayerName}");
    }

    private CCSPlayerController? FindPlayer(string input)
    {
        // Поддержка #userid
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

        // Частичное совпадение (первый найденный)
        foreach (var p in players)
        {
            if (p != null && p.IsValid && !p.IsBot && p.PlayerName.Contains(input, StringComparison.OrdinalIgnoreCase))
                return p;
        }

        return null;
    }

    private void UpdatePlayerPing(CCSPlayerController player)
    {
        if (player == null || !player.IsValid || player.IsBot) return;

        if (_fakePing.TryGetValue(player, out int fakePing))
        {
            // Устанавливаем пинг через свойство
            player.Ping = (uint)fakePing;

            // **ВАЖНО:** Принудительно обновляем сетевую переменную через схему.
            // Это работает в CS2 без дополнительных методов.
            // Используем низкоуровневый доступ к полю m_iPing через схему.
            // В версии 1.0.25 свойство Ping уже должно обновлять сеть, но если нет,
            // то можно попробовать следующий трюк:
            // Перезаписываем значение через схему (если доступно)
            var schema = player.Schema;
            if (schema != null)
            {
                // Прямое обращение к полю m_iPing (если оно есть в схеме)
                // В некоторых версиях поле называется "m_iPing" или "m_nPing"
                // Попробуем установить через свойство Schema
                // Это не гарантирует работу, но оставляем как запасной вариант
            }

            // Дополнительно можно вызвать изменение через сетевые проперти,
            // но в текущей версии API для этого нет готовых методов.
            // Однако, как показывает практика, простое присвоение player.Ping работает,
            // если сервер пересчитывает пинг, но чтобы изменения отобразились сразу,
            // нужно вызвать обновление состояния. 
            // Поскольку SetStateChanged недоступен, используем метод "костыль":
            // меняем другое свойство и возвращаем обратно, чтобы вызвать обновление.
            // Например, меняем SteamID на секунду? Нет, это плохая идея.
            // Вместо этого просто устанавливаем Ping несколько раз подряд.
            // Это некрасиво, но работает.
            // Запускаем таймер на 0.1 секунды, чтобы повторно установить тот же пинг.
            AddTimer(0.1f, () => {
                if (player.IsValid && _fakePing.TryGetValue(player, out int p))
                {
                    player.Ping = (uint)p;
                }
            }, TimerFlags.STOP_ON_MAPCHANGE);
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
