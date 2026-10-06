using System;
using System.Collections.Generic;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using warwthtreason.Common;

namespace warwthtreason.Server
{
    // Статический аксессор — Harmony-патчи из других файлов достают через него
    // активный экземпляр системы, так как сами патчи статические.
    public static class CombatLogSystemAccessor
    {
        public static CombatLogSystem? Instance { get; set; }
    }

    public class CombatLogSystem : ModSystem
    {
        // === Основные ссылки ===
        private ICoreServerAPI sapi = null!;
        private CombatLogConfig config = null!;
        private ClaimFlagsConfig claimFlagsConfig = null!;

        // === Состояние боя ===
        // Ключ — PlayerUID, значение — время окончания боя (ElapsedMilliseconds + duration).
        private Dictionary<string, long> combatTimers = new Dictionary<string, long>();

        // === Инфраструктура ===
        private long tickListenerId;
        private Harmony? harmony;
        private IServerNetworkChannel? serverChannel;

        // === Публичные свойства для патчей и клиента ===
        public CombatLogConfig Config => config;
        public ICoreServerAPI Sapi => sapi;

        // Публичная версия переключения флага, используемая слушателем CommandHook.
        // Принимает готовое значение (true = разрешено, false = запрещено).
        public TextCommandResult ToggleClaimFlagDirect(IServerPlayer player, bool isPvP, bool value)
        {
            var claims = sapi.World.Claims.Get(player.Entity.Pos.AsBlockPos);
            if (claims == null || claims.Length == 0)
                return TextCommandResult.Error("Вы не находитесь в привате.");

            string claimId = GetClaimId(claims[0]);
            if (!claimFlagsConfig.Flags.TryGetValue(claimId, out var flags))
            {
                flags = new ClaimFlags();
                claimFlagsConfig.Flags[claimId] = flags;
            }

            if (isPvP) flags.AllowPvP = value;
            else flags.AllowPvE = value;

            sapi.StoreModConfig(claimFlagsConfig, "ClaimFlags.json");

            string flagName = isPvP ? "PvP" : "PvE";
            string state = value ? "разрешён" : "запрещён";
            return TextCommandResult.Success($"Флаг {flagName} для привата '{claimId}' {state}.");
        }

        public override void StartServerSide(ICoreServerAPI api)
        {
            sapi = api;

            // --- Загрузка основного конфига ---
            config = sapi.LoadModConfig<CombatLogConfig>("WarWithoutReason.json");
            if (config == null)
            {
                config = new CombatLogConfig();
                sapi.Logger.Notification("[WarWithoutReason] Создан новый конфиг с настройками по умолчанию.");
            }
            else if (config.CombatDurationSeconds <= 0)
            {
                config.CombatDurationSeconds = 30;
                sapi.Logger.Warning("[WarWithoutReason] CombatDurationSeconds <= 0, установлено 30.");
            }
            sapi.StoreModConfig(config, "WarWithoutReason.json");

            // --- Загрузка per-claim флагов ---
            claimFlagsConfig = sapi.LoadModConfig<ClaimFlagsConfig>("ClaimFlags.json");
            if (claimFlagsConfig == null)
            {
                claimFlagsConfig = new ClaimFlagsConfig();
                sapi.StoreModConfig(claimFlagsConfig, "ClaimFlags.json");
            }

            // --- Проверка SafeZone ---
            // Если SafeZone установлен — отключаем нашу защиту, чтобы не было двойной обработки.
            if (sapi.ModLoader.IsModEnabled("safezone"))
            {
                sapi.Logger.Notification("[WarWithoutReason] Обнаружен SafeZone. Собственная защита приватов отключена.");
                config.EnableClaimProtection = false;
                sapi.StoreModConfig(config, "WarWithoutReason.json");
            }
            else
            {
                sapi.Logger.Notification("[WarWithoutReason] SafeZone не обнаружен. Активирована собственная защита приватов.");
            }

            // --- Подписки на события ---
            sapi.Event.PlayerJoin += OnPlayerJoin;
            sapi.Event.PlayerLeave += OnPlayerLeave;
            tickListenerId = sapi.Event.RegisterGameTickListener(OnGameTick, 1000);

            // --- Сетевой канал для HUD-таймера ---
            serverChannel = sapi.Network.RegisterChannel("warwthtreason")
                .RegisterMessageType<CombatTimerPacket>();

            // --- Harmony ---
            harmony = new Harmony(Mod.Info.ModID);
            harmony.PatchAll();
            CombatLogSystemAccessor.Instance = this;

            // --- Команды /claimflag ---
            RegisterClaimFlagCommands();

            // --- Интеграция с CommandHook ---
            //RegisterCommandHookListener();

            sapi.Logger.Notification("[WarWithoutReason] Серверная часть мода успешно загружена.");
        }

        // Регистрирует команды /claimflag pvp|pve|status.
        private void RegisterClaimFlagCommands()
        {
            sapi.ChatCommands.Create("claimflag")
                .WithDescription("Управление флагами PvP/PvE для вашего привата.")
                .BeginSubCommand("pvp")
                    .WithDescription("Включить/выключить PvP: /claimflag pvp on|off|default")
                    .WithArgs(sapi.ChatCommands.Parsers.Word("value"))
                    .HandleWith(args => ToggleClaimFlag(args, isPvP: true))
                .EndSubCommand()
                .BeginSubCommand("pve")
                    .WithDescription("Включить/выключить урон от мобов: /claimflag pve on|off|default")
                    .WithArgs(sapi.ChatCommands.Parsers.Word("value"))
                    .HandleWith(args => ToggleClaimFlag(args, isPvP: false))
                .EndSubCommand()
                .BeginSubCommand("status")
                    .WithDescription("Показать текущие флаги привата.")
                    .HandleWith(OnShowClaimStatus)
                .EndSubCommand();
        }

        // Регистрирует слушателя CommandHook, если мод установлен.
        /*private void RegisterCommandHookListener()
        {
            if (!sapi.ModLoader.IsModEnabled("commandhook"))
            {
                sapi.Logger.Notification("[WarWithoutReason] CommandHook не найден. Команды /land claim allowpvp/allowpve недоступны.");
                return;
            }

            try
            {
                // Регистрируем слушателя через CommandHook API.
                // LandClaimCommandListener — наш класс, реализующий ICommandListener.
                CommandHook.CommandHookModSystem.Register(new LandClaimCommandListener(this));
                sapi.Logger.Notification("[WarWithoutReason] Интеграция с CommandHook активна.");
            }
            catch (Exception ex)
            {
                sapi.Logger.Error("[WarWithoutReason] Ошибка регистрации в CommandHook: " + ex.Message);
            }
        }*/

        // === Работа с игроками ===

        private void OnPlayerJoin(IServerPlayer player)
        {
            var healthBehavior = player.Entity.GetBehavior<EntityBehaviorHealth>();
            if (healthBehavior != null)
            {
                // Передаём игрока-жертву в обработчик через лямбду — событие onDamaged
                // само по себе жертву не передаёт.
                healthBehavior.onDamaged += (damage, damageSource) => OnPlayerDamaged(player, damage, damageSource);
            }
        }

        private void OnPlayerLeave(IServerPlayer player)
        {
            if (combatTimers.ContainsKey(player.PlayerUID))
            {
                combatTimers.Remove(player.PlayerUID);

                if (config.KillOnCombatLogout)
                {
                    KillCombatLogger(player);
                }
                else
                {
                    sapi.Logger.Notification(
                        $"[WarWithoutReason] Player '{player.PlayerName}' ({player.PlayerUID}) " +
                        $"вышел из игры во время боя. Наказание не применено (KillOnCombatLogout = false).");
                }
            }
        }

        // === Combat Log ===

        private float OnPlayerDamaged(IServerPlayer victim, float damage, DamageSource damageSource)
        {
            if (damageSource.Source != EnumDamageSource.Player) return damage;

            var attackerEntity = damageSource.GetCauseEntity() as EntityPlayer;
            if (attackerEntity?.Player is not IServerPlayer attacker) return damage;
            if (attacker == victim) return damage;

            SetCombatMode(victim);
            SetCombatMode(attacker);
            return damage;
        }

        private bool SetCombatMode(IServerPlayer player)
        {
            bool wasAlreadyInCombat = combatTimers.ContainsKey(player.PlayerUID);
            long endTime = sapi.World.ElapsedMilliseconds + (config.CombatDurationSeconds * 1000L);
            combatTimers[player.PlayerUID] = endTime;

            if (!wasAlreadyInCombat)
            {
                SendCombatNotification(player, true);
                return true;
            }
            return false;
        }

        private void SendCombatNotification(IServerPlayer player, bool inCombat)
        {
            if (config.SendChatMessages)
            {
                string langKey = inCombat ? "warwthtreason:combat-enter" : "warwthtreason:combat-exit";
                string chatMessage = Lang.GetL(player.LanguageCode, langKey);
                player.SendMessage(GlobalConstants.GeneralChatGroup, chatMessage, EnumChatType.Notification);
            }

            if (config.ShowScreenMessages)
            {
                string langKey = inCombat ? "warwthtreason:combat-screen-enter" : "warwthtreason:combat-screen-exit";
                string screenMessage = Lang.GetL(player.LanguageCode, langKey);

                int color = inCombat
                    ? ColorUtil.ColorFromRgba(255, 0, 0, 255)
                    : ColorUtil.ColorFromRgba(0, 255, 0, 255);

                player.SendIngameError("combat_status", screenMessage, color);
            }
        }

        // === Тик и таймеры ===

        private void OnGameTick(float dt)
        {
            long currentTime = sapi.World.ElapsedMilliseconds;
            List<string> expiredPlayers = new List<string>();

            foreach (var kvp in combatTimers)
            {
                string playerUid = kvp.Key;
                long endTime = kvp.Value;

                var player = sapi.World.PlayerByUid(playerUid) as IServerPlayer;
                if (player == null)
                {
                    expiredPlayers.Add(playerUid);
                    continue;
                }

                long remainingMs = endTime - currentTime;

                if (remainingMs <= 0)
                {
                    serverChannel?.SendPacket(new CombatTimerPacket { RemainingSeconds = 0 }, player);
                    expiredPlayers.Add(playerUid);
                    continue;
                }

                int seconds = (int)Math.Ceiling(remainingMs / 1000.0);
                serverChannel?.SendPacket(new CombatTimerPacket { RemainingSeconds = seconds }, player);
            }

            foreach (string uid in expiredPlayers)
            {
                combatTimers.Remove(uid);
                var player = sapi.World.PlayerByUid(uid) as IServerPlayer;
                if (player != null)
                {
                    SendCombatNotification(player, false);
                }
            }
        }

        // === Наказание за combat logging ===

        private void KillCombatLogger(IServerPlayer player)
        {
            var entity = player.Entity;
            if (entity == null || !entity.Alive) return;

            // DamageSource типа Suicide — игра корректно обработает смерть
            // и не припишет убийство атакующему.
            var damageSource = new DamageSource
            {
                Source = EnumDamageSource.Suicide,
                Type = EnumDamageType.Injury,
                SourceEntity = entity,
                CauseEntity = entity
            };

            entity.Die(EnumDespawnReason.Death, damageSource);

            sapi.Logger.Notification(
                $"[WarWithoutReason] Player '{player.PlayerName}' ({player.PlayerUID}) " +
                $"вышел из игры во время боя и был убит.");

            string message = Lang.Get("warwthtreason:combat-log-kill", player.PlayerName);
            foreach (var onlinePlayer in sapi.World.AllOnlinePlayers)
            {
                if (onlinePlayer is IServerPlayer sp)
                {
                    sp.SendMessage(GlobalConstants.GeneralChatGroup, message, EnumChatType.Notification);
                }
            }
        }

        // === Публичные методы для патчей и слушателя ===

        // Проверяет, находится ли игрок в режиме боя.
        public bool IsInCombat(IServerPlayer player) => combatTimers.ContainsKey(player.PlayerUID);

        // Возвращает массив приватов в указанной позиции (может быть null или пустым).
        public LandClaim[]? GetClaimsAt(BlockPos pos) => sapi.World.Claims.Get(pos);

        // Генерирует уникальный ID привата из его координат.
        // В публичном API нет строкового ID, поэтому используем координаты.
        public string GetClaimId(LandClaim claim)
        {
            return claim.ToString()!;
        }

        // Возвращает per-claim флаги или null, если для привата ничего не задано.
        public ClaimFlags? GetClaimFlags(LandClaim claim)
        {
            if (claim == null) return null;
            string id = GetClaimId(claim);
            return claimFlagsConfig.Flags.TryGetValue(id, out var flags) ? flags : null;
        }

        // === Обработчики команд /claimflag ===

        private TextCommandResult ToggleClaimFlag(TextCommandCallingArgs args, bool isPvP)
        {
            if (args.Caller.Player is not IServerPlayer player)
                return TextCommandResult.Error("Команда доступна только игрокам.");

            var claims = sapi.World.Claims.Get(player.Entity.Pos.AsBlockPos);
            if (claims == null || claims.Length == 0)
                return TextCommandResult.Error("Вы не находитесь в привате.");

            var claim = claims[0];

            // TryAccess возвращает true для владельца и игроков с правами на строительство.
            bool hasAccess = sapi.World.Claims.TryAccess(
                player, player.Entity.Pos.AsBlockPos, EnumBlockAccessFlags.BuildOrBreak);

            if (!hasAccess)
                return TextCommandResult.Error("У вас нет прав на управление этим приватом.");

            string claimId = claim.ToString()!;
            if (!claimFlagsConfig.Flags.TryGetValue(claimId, out var flags))
            {
                flags = new ClaimFlags();
                claimFlagsConfig.Flags[claimId] = flags;
            }

            string value = (args[0] as string ?? "").ToLowerInvariant();
            bool? newValue;
            switch (value)
            {
                case "on": newValue = true; break;
                case "off": newValue = false; break;
                case "default": newValue = null; break;
                default:
                    return TextCommandResult.Error("Используйте: on, off или default.");
            }

            if (isPvP) flags.AllowPvP = newValue;
            else flags.AllowPvE = newValue;

            sapi.StoreModConfig(claimFlagsConfig, "ClaimFlags.json");

            string flagName = isPvP ? "PvP" : "PvE";
            string state = value == "default" ? "сброшен в глобальное значение" : $"установлен в '{value}'";
            return TextCommandResult.Success($"Флаг {flagName} для привата '{claimId}' {state}.");
        }

        private TextCommandResult OnShowClaimStatus(TextCommandCallingArgs args)
        {
            if (args.Caller.Player is not IServerPlayer player)
                return TextCommandResult.Error("Команда доступна только игрокам.");

            var claims = sapi.World.Claims.Get(player.Entity.Pos.AsBlockPos);
            if (claims == null || claims.Length == 0)
                return TextCommandResult.Error("Вы не находитесь в привате.");

            string claimId = GetClaimId(claims[0]);

            if (!claimFlagsConfig.Flags.TryGetValue(claimId, out var flags))
                return TextCommandResult.Success($"Приват '{claimId}': флаги не заданы. Используются глобальные настройки.");

            string pvp = flags.AllowPvP?.ToString() ?? "по умолчанию";
            string pve = flags.AllowPvE?.ToString() ?? "по умолчанию";

            return TextCommandResult.Success($"Приват '{claimId}': PvP = {pvp}, PvE = {pve}.");
        }

        // === Выгрузка ===

        public override void Dispose()
        {
            if (sapi != null)
            {
                sapi.Event.PlayerJoin -= OnPlayerJoin;
                sapi.Event.PlayerLeave -= OnPlayerLeave;
                sapi.Event.UnregisterGameTickListener(tickListenerId);
            }

            harmony?.UnpatchAll(Mod.Info.ModID);
            CombatLogSystemAccessor.Instance = null;

            sapi?.Logger.Notification("[WarWithoutReason] Серверная часть мода выгружена.");
        }
    }
}