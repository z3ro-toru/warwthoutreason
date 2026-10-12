using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

using warwthtreason.Common;

namespace warwthtreason.Server
{
    // Player-facing logic: join/leave handling, PvP damage detection,
    // combat timer bookkeeping, and per-second tick processing.
    public partial class CombatLogSystem
    {

        

        // === Player events ===

        private void OnPlayerJoin(IServerPlayer player)
        {
            if (player == null) return;

            // DeadHand handles the combat-log punishment queue and name caching.
            deadHand.OnPlayerJoin(player);
            playerStats.OnPlayerJoin(player);

            var entity = player.Entity;
            if (entity == null) return;

            var healthBehavior = entity.GetBehavior<EntityBehaviorHealth>();
            if (healthBehavior == null) return;

            // Store the delegate so it can be unsubscribed later.
            OnDamagedDelegate handler = (damage, damageSource) => OnPlayerDamaged(player, damage, damageSource);
            damageHandlers[player.PlayerUID] = handler;
            healthBehavior.onDamaged += handler;
        }

        private void OnPlayerLeave(IServerPlayer player)
        {
            if (player == null) return;

            // Unsubscribe from onDamaged before the entity is destroyed.
            var entity = player.Entity;
            if (entity != null)
            {
                var healthBehavior = entity.GetBehavior<EntityBehaviorHealth>();
                if (healthBehavior != null && damageHandlers.TryGetValue(player.PlayerUID, out var handler))
                {
                    healthBehavior.onDamaged -= handler;
                }
            }
            damageHandlers.Remove(player.PlayerUID);

            // Extract the remaining time BEFORE removing the entry from the dictionary.
            int remainingSeconds = 0;
            bool wasInCombat = combatTimers.TryGetValue(player.PlayerUID, out long endTime);
            if (wasInCombat)
            {
                remainingSeconds = (int)Math.Ceiling((endTime - sapi.World.ElapsedMilliseconds) / 1000.0);
                combatTimers.Remove(player.PlayerUID);
            }

            // DeadHand decides what to do based on the current policy.
            deadHand.OnPlayerLeave(player, wasInCombat, remainingSeconds);
        }

        // === Combat log ===

        private float OnPlayerDamaged(IServerPlayer victim, float damage, DamageSource damageSource)
        {
            if (damageSource.Source != EnumDamageSource.Player) return damage;

            var attackerEntity = damageSource.GetCauseEntity() as EntityPlayer;
            if (attackerEntity?.Player is not IServerPlayer attacker) return damage;
            if (attacker == victim) return damage;

            // Snapshot pre-hit state, so the stats manager can tell initiations
            // apart from continuations. SetCombatMode flips these below.
            bool attackerWasInCombat = combatTimers.ContainsKey(attacker.PlayerUID);
            bool victimWasInCombat = combatTimers.ContainsKey(victim.PlayerUID);

            playerStats.OnPlayerDamaged(attacker, victim, attackerWasInCombat, victimWasInCombat);

            SetCombatMode(victim);
            SetCombatMode(attacker);
            return damage;
        }

        private void OnEntityDeath(Entity entity, DamageSource damageSource)
        {
            playerStats.OnEntityDeath(entity, damageSource);
        }

        // Marks the player as in-combat and refreshes the end time.
        // Returns true if this was the first hit (state change).
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

                player.SendIngameError(Constants.ScreenMessageKey, screenMessage, color);
            }
        }

        // === Tick ===

        private void OnGameTick(float dt)
        {
            long currentTime = sapi.World.ElapsedMilliseconds;
            List<string> expiredPlayers = [];

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
                    // Send 0 so the client hides the HUD timer.
                    serverChannel?.SendPacket(new CombatTimerPacket { RemainingSeconds = 0 }, player);
                    expiredPlayers.Add(playerUid);
                    continue;
                }

                // Round up so the player sees at least 1 second.
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

        // === Public helpers for patches ===

        // True if the player is currently flagged as in combat.
        public void RestoreCombatTimer(IServerPlayer player, int seconds)
        {
            if (player == null || seconds <= 0) return;

            long endTime = sapi.World.ElapsedMilliseconds + (seconds * 1000L);
            combatTimers[player.PlayerUID] = endTime;

            // Notify with a dedicated key that explains the timer was kept.
            if (config.SendChatMessages)
            {
                string message = Lang.GetL(player.LanguageCode, "warwthtreason:combat-kept-timer", seconds);
                player.SendMessage(GlobalConstants.GeneralChatGroup, message, EnumChatType.Notification);
            }

            // Red screen banner — reuse the standard enter message.
            if (config.ShowScreenMessages)
            {
                string screenMessage = Lang.GetL(player.LanguageCode, "warwthtreason:combat-screen-enter");
                int color = ColorUtil.ColorFromRgba(255, 0, 0, 255);
                player.SendIngameError("combat_status", screenMessage, color);
            }
        }
    }
}