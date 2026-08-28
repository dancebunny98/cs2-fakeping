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
    public override string ModuleVersion => "2.0.0";
    public override string ModuleAuthor => "Your Name";

    private Dictionary<CCSPlayerController, FakePingData> _fakePingData = new();
    private Timer? _updateTimer;

    public override void Load(bool hotReload)
    {
        AddCommand("css_fakeping", "Set fake ping. Usage: !fakeping <player> <ping> OR !fakeping <player> <min-max> <interval>", OnFakePingCommand);
        AddCommand("css_fakeping_remove", "Remove fake ping. Usage: !fakeping_remove <player>", OnFakePingRemoveCommand);

        RegisterEventHandler<EventPlayerConnectFull>(OnPlayerConnectFull);
        RegisterEventHandler<EventPlayerDisconnect>(OnPlayerDisconnect);

        // Запускаем таймер для обновления динамического пинга каждую секунду
        _updateTimer = AddTimer(1.0f, UpdateDynamicPings, TimerFlags.REPEAT);
    }

    public override void Unload(bool hotReload)
    {
        _updateTimer?.Kill();
        _fakePingData.Clear();
    }

    [RequiresPermissions("@css/root")]
    [CommandHelper(minArgs: 2, usage: "<player> <ping> OR <player> <min-max> <interval>")]
    private void OnFakePingCommand(CCSPlayerController? caller, CommandInfo command)
    {
        var target = FindPlayer(command.GetArg(1));
        if (target == null)
        {
            command.ReplyToCommand($" {ChatColors.Red} Player not found.");
            return;
        }

        // Если аргументов 3 – динамический режим
        if (command.ArgCount >= 4)
        {
            string rangeArg = command.GetArg(2);
            string intervalArg = command.GetArg(3);

            if (!int.TryParse(intervalArg, out int interval) || interval < 1)
            {
                command.ReplyToCommand($" {ChatColors.Red} Interval must be >= 1 second.");
                return;
            }

            var parts = rangeArg.Split('-');
            if (parts.Length != 2 || !int.TryParse(parts[0], out int min) || !int.TryParse(parts[1], out int max) || min > max || min < 0 || max > 4095)
            {
                command.ReplyToCommand($" {ChatColors.Red} Invalid range. Use format: min-max (e.g. 10-50).");
                return;
            }

            var data = new FakePingData
            {
                IsDynamic = true,
                MinPing = min,
                MaxPing = max,
                IntervalSeconds = interval,
                NextUpdateTime = 0, // обновится сразу
                CurrentPing = 0
            };
            _fakePingData[target] = data;
            command.ReplyToCommand($" {ChatColors.Green} Dynamic fake ping enabled for {target.PlayerName}: range {min}-{max} ms, change every {interval} sec.");
            return;
        }

        // Иначе – статичный режим (2 аргумента: игрок и пинг)
        if (!int.TryParse(command.GetArg(2), out int ping) || ping < 0 || ping > 4095)
        {
            command.ReplyToCommand($" {ChatColors.Red} Ping must be 0-4095.");
            return;
        }

        var staticData = new FakePingData
        {
            IsDynamic = false,
            StaticPing = ping,
            CurrentPing = ping
        };
        _fakePingData[target] = staticData;
        command.ReplyToCommand($" {ChatColors.Green} Static fake ping set to {ping} ms for {target.PlayerName}");
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

        if (_fakePingData.ContainsKey(target))
            _fakePingData.Remove(target);

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

    // Обновление динамических пингов (вызывается раз в секунду)
    private void UpdateDynamicPings()
    {
        float currentTime = (float)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;

        foreach (var kvp in _fakePingData)
        {
            var player = kvp.Key;
            var data = kvp.Value;

            if (!player.IsValid || player.IsBot || !_fakePingData.ContainsKey(player))
                continue;

            if (data.IsDynamic)
            {
                if (currentTime >= data.NextUpdateTime)
                {
                    // Генерируем случайный пинг в диапазоне
                    Random rand = new Random();
                    data.CurrentPing = rand.Next(data.MinPing, data.MaxPing + 1);
                    data.NextUpdateTime = currentTime + data.IntervalSeconds;
                }
            }
        }
    }

    // OnTick – применяем текущий пинг для всех игроков с фейком
    private void OnTick()
    {
        foreach (var kvp in _fakePingData)
        {
            var player = kvp.Key;
            var data = kvp.Value;

            if (player == null || !player.IsValid || player.IsBot)
                continue;

            // Применяем актуальный пинг
            player.Ping = (uint)data.CurrentPing;
            // Дублируем для надёжности
            player.Ping = (uint)data.CurrentPing;
        }
    }

    // Хуки событий
    private HookResult OnPlayerConnectFull(EventPlayerConnectFull @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player != null && _fakePingData.ContainsKey(player))
            _fakePingData.Remove(player);
        return HookResult.Continue;
    }

    private HookResult OnPlayerDisconnect(EventPlayerDisconnect @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player != null && _fakePingData.ContainsKey(player))
            _fakePingData.Remove(player);
        return HookResult.Continue;
    }

    // Регистрируем OnTick в Load
    public override void Load(bool hotReload)
    {
        base.Load(hotReload);
        // ... остальной код (команды, события) – как выше
        // Добавляем:
        RegisterListener<Listeners.OnTick>(OnTick);
    }

    public override void Unload(bool hotReload)
    {
        // Очищаем ресурсы
        _updateTimer?.Kill();
        _fakePingData.Clear();
        base.Unload(hotReload);
    }
}

// Класс для хранения данных фейк-пинга
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
