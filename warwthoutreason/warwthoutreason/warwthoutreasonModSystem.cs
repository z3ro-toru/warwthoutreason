using System;
using System.Collections.Generic;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace CombatLogMod
{
    // Отдельный статический класс-аксессор. Патч SafeZone будет обращаться сюда,
    // чтобы получить доступ к текущему экземпляру системы CombatLogSystem.
    // Статическое поле нужно потому, что Harmony-патчи статические и не могут 
    // напрямую ссылаться на экземпляры объектов.
    
    public static class CombatLogSystemAccessor
    {
        // Ссылка на активный экземпляр системы. Устанавливается в StartServerSide.
        public static CombatLogSystem? Instance { get; set; }
    }

    public class CombatLogSystem : ModSystem
    {
        // Ссылка на API сервера.
        private ICoreServerAPI sapi = null!;

        // Загруженная конфигурация.
        private CombatLogConfig config = null!;

        // Словарь с временем окончания боя для каждого игрока.
        private Dictionary<string, long> combatTimers = new Dictionary<string, long>();

        // ID слушателя тиков.
        private long tickListenerId;

        // Ссылка на объект Harmony для управления патчами.
        private Harmony? harmony;

        public override void StartServerSide(ICoreServerAPI api)
        {
            sapi = api;

            // Загрузка/создание конфига.
            config = sapi.LoadModConfig<CombatLogConfig>("CombatLogMod.json");
            if (config == null)
            {
                config = new CombatLogConfig();
                sapi.StoreModConfig(config, "CombatLogMod.json");
            }

            // Подписки на события игроков.
            sapi.Event.PlayerJoin += OnPlayerJoin;
            sapi.Event.PlayerLeave += OnPlayerLeave;

            // Тик раз в секунду для проверки истечения таймеров.
            tickListenerId = sapi.Event.RegisterGameTickListener(OnGameTick, 1000);

            // --- ИНТЕГРАЦИЯ С SAFEZONE ---
            // Создаём экземпляр Harmony с уникальным идентификатором.
            // Этот ID используется для снятия именно наших патчей при выгрузке мода.
            harmony = new Harmony("com.combatlogmod.patch");

            // Применяем все патчи, помеченные атрибутом [HarmonyPatch] в этой сборке.
            // Это активирует SafeZoneCombatPatch (см. отдельный файл).
            harmony.PatchAll();

            // Сохраняем ссылку на этот экземпляр системы,
            // чтобы Harmony-патч мог вызывать IsInCombat().
            CombatLogSystemAccessor.Instance = this;
        }

        private void OnPlayerJoin(IServerPlayer player)
        {
            var healthBehavior = player.Entity.GetBehavior<EntityBehaviorHealth>();
            if (healthBehavior != null)
            {
                // ВАЖНО: Мы используем лямбда-выражение, чтобы "прокинуть" внутрь
                // игрока-жертву (player). Событие onDamaged само по себе
                // не передаёт жертву, только урон и DamageSource.
                healthBehavior.onDamaged += (damage, damageSource) => OnPlayerDamaged(player, damage, damageSource);
            }
        }

        private void OnPlayerLeave(IServerPlayer player)
        {
            combatTimers.Remove(player.PlayerUID);
        }

        // Обработчик получения урона. Теперь принимает игрока-жертву как параметр.
        private float OnPlayerDamaged(IServerPlayer victim, float damage, DamageSource damageSource)
        {
            // Нас интересует только PvP-урон.
            if (damageSource.Source != EnumDamageSource.Player) return damage;

            // Получаем атакующего из DamageSource.
            var attackerEntity = damageSource.GetCauseEntity() as EntityPlayer;
            if (attackerEntity?.Player is not IServerPlayer attacker) return damage;

            // Игнорируем урон самому себе.
            if (attacker == victim) return damage;

            // Активируем/продлеваем режим боя для обоих.
            SetCombatMode(victim);
            SetCombatMode(attacker);

            // Возвращаем урон без изменений.
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
                if (currentTime >= kvp.Value)
                {
                    expiredPlayers.Add(kvp.Key);
                }
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

        // Публичный метод, который вызывает Harmony-патч SafeZone.
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

            // Снимаем все Harmony-патчи, установленные нашим модом.
            // Это важно, чтобы при перезагрузке мода не остались "висячие" патчи.
            harmony?.UnpatchAll("com.combatlogmod.patch");

            // Обнуляем ссылку, чтобы патч не обращался к мёртвому объекту.
            CombatLogSystemAccessor.Instance = null;
        }
    }
}