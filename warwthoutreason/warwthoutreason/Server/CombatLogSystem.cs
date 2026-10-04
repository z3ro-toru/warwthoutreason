using System;
using System.Collections.Generic;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using warwthtreason.Common; // Доступ к CombatTimerPacket

namespace warwthtreason.Server
{
    // Статический аксессор — Harmony-патчи (в другом файле) достают через него наш экземпляр системы.
    public static class CombatLogSystemAccessor
    {
        public static CombatLogSystem? Instance { get; set; }
    }

    public class CombatLogSystem : ModSystem
    {
        private ICoreServerAPI sapi = null!;
        private CombatLogConfig config = null!;
        private Dictionary<string, long> combatTimers = new Dictionary<string, long>();
        private long tickListenerId;
        private Harmony? harmony;
        private IServerNetworkChannel? serverChannel;

        public override void StartServerSide(ICoreServerAPI api)
        {
            sapi = api;

            // Загружаем конфиг под новым именем файла.
            config = sapi.LoadModConfig<CombatLogConfig>("WarWithoutReason.json");
            if (config == null)
            {
                config = new CombatLogConfig();
                sapi.StoreModConfig(config, "WarWithoutReason.json");
            }

            sapi.Event.PlayerJoin += OnPlayerJoin;
            sapi.Event.PlayerLeave += OnPlayerLeave;
            tickListenerId = sapi.Event.RegisterGameTickListener(OnGameTick, 1000);

            // Регистрируем сетевой канал. Имя "warwthtreason" — должно совпадать с клиентом.
            serverChannel = sapi.Network.RegisterChannel("warwthtreason")
                .RegisterMessageType<CombatTimerPacket>();

            // Harmony с новым уникальным ID.
            harmony = new Harmony("com.warwthtreason.patch");
            harmony.PatchAll();

            CombatLogSystemAccessor.Instance = this;
        }

        private void OnPlayerJoin(IServerPlayer player)
        {
            var healthBehavior = player.Entity.GetBehavior<EntityBehaviorHealth>();
            if (healthBehavior != null)
            {
                healthBehavior.onDamaged += (damage, damageSource) => OnPlayerDamaged(player, damage, damageSource);
            }
        }

        private void OnPlayerLeave(IServerPlayer player)
        {
            combatTimers.Remove(player.PlayerUID);
        }

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
            long endTime = sapi.World.ElapsedMilliseconds + (config.CombatDurationSeconds * 1000);
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
                string chatMessage = inCombat
                    ? "[Combat] Вы вступили в бой! Безопасные зоны больше не защищают вас."
                    : "[Combat] Вы вышли из режима боя. Безопасные зоны снова активны.";

                player.SendMessage(GlobalConstants.GeneralChatGroup, chatMessage, EnumChatType.Notification);
            }

            if (config.ShowScreenMessages)
            {
                string screenMessage = inCombat
                    ? "РЕЖИМ БОЯ АКТИВИРОВАН"
                    : "РЕЖИМ БОЯ ОКОНЧЕН";

                int color = inCombat
                    ? ColorUtil.ColorFromRgba(255, 0, 0, 255)
                    : ColorUtil.ColorFromRgba(0, 255, 0, 255);

                player.SendIngameError("combat_status", screenMessage, color);
            }
        }

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

        public bool IsInCombat(IServerPlayer player)
        {
            return combatTimers.ContainsKey(player.PlayerUID);
        }

        public override void Dispose()
        {
            if (sapi != null)
            {
                sapi.Event.PlayerJoin -= OnPlayerJoin;
                sapi.Event.PlayerLeave -= OnPlayerLeave;
                sapi.Event.UnregisterGameTickListener(tickListenerId);
            }

            harmony?.UnpatchAll("com.warwthtreason.patch");
            CombatLogSystemAccessor.Instance = null;
        }
    }
}