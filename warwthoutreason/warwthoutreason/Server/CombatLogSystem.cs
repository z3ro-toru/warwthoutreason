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
    public static class CombatLogSystemAccessor
    {
        public static CombatLogSystem? Instance { get; set; }
    }

    public class CombatLogSystem : ModSystem
    {
        // === Core references ===
        private ICoreServerAPI sapi = null!;
        private CombatLogConfig config = null!;
        private ClaimFlagsConfig claimFlagsConfig = null!;

        // === Combat state ===
        // Key: PlayerUID. Value: combat end time (ElapsedMilliseconds + duration).
        private readonly Dictionary<string, long> combatTimers = [];

        // Stores the exact delegate we subscribed to onDamaged, so we can
        // unsubscribe later. Without this, we would leak subscriptions and
        // potentially get double-handling on reconnect.
        private readonly Dictionary<string, OnDamagedDelegate> damageHandlers = [];

        // === Infrastructure ===
        private long tickListenerId;
        private Harmony? harmony;
        private IServerNetworkChannel? serverChannel;

        // === Public accessors for patches and the client ===
        public CombatLogConfig Config => config;
        public ICoreServerAPI Sapi => sapi;

        // Public flag toggle used by the optional CommandHook listener.
        public TextCommandResult ToggleClaimFlagDirect(IServerPlayer player, bool isPvP, bool value)
        {
            var claims = sapi.World.Claims.Get(player.Entity.Pos.AsBlockPos);
            if (claims == null || claims.Length == 0)
                return TextCommandResult.Error(Lang.GetL(player.LanguageCode, "warwthtreason:err-not-in-claim"));

            string claimId = GetClaimId(claims[0]);
            if (!claimFlagsConfig.Flags.TryGetValue(claimId, out var flags))
            {
                flags = new ClaimFlags();
                claimFlagsConfig.Flags[claimId] = flags;
            }

            if (isPvP) flags.AllowPvP = value;
            else flags.AllowPvE = value;

            sapi.StoreModConfig(claimFlagsConfig, "WWR_ClaimFlags.json");

            string flagName = isPvP ? "PvP" : "PvE";
            string state = value
                ? Lang.GetL(player.LanguageCode, "warwthtreason:value-allowed")
                : Lang.GetL(player.LanguageCode, "warwthtreason:value-denied");
            return TextCommandResult.Success(Lang.GetL(player.LanguageCode, "warwthtreason:flag-set", flagName, claimId, state));
        }

        public override void StartServerSide(ICoreServerAPI api)
        {
            sapi = api;

            // --- Load main config ---
            config = sapi.LoadModConfig<CombatLogConfig>("WarWithoutReason.json");
            if (config == null)
            {
                config = new CombatLogConfig();
                sapi.Logger.Notification("[WarWithoutReason] Created a new config with default values.");
            }
            else if (config.CombatDurationSeconds <= 0)
            {
                config.CombatDurationSeconds = 30;
                sapi.Logger.Warning("[WarWithoutReason] CombatDurationSeconds <= 0, reset to 30.");
            }
            sapi.StoreModConfig(config, "WarWithoutReason.json");

            // --- Load per-claim flags ---
            claimFlagsConfig = sapi.LoadModConfig<ClaimFlagsConfig>("WWR_ClaimFlags.json");
            if (claimFlagsConfig == null)
            {
                claimFlagsConfig = new ClaimFlagsConfig();
                sapi.StoreModConfig(claimFlagsConfig, "WWR_ClaimFlags.json");
            }

            // --- SafeZone detection ---
            if (sapi.ModLoader.IsModEnabled("safezone"))
            {
                sapi.Logger.Notification("[WarWithoutReason] SafeZone detected. Built-in claim protection disabled.");
                config.EnableClaimProtection = false;
                sapi.StoreModConfig(config, "WarWithoutReason.json");
            }
            else
            {
                sapi.Logger.Notification("[WarWithoutReason] SafeZone not detected. Built-in claim protection enabled.");
            }

            // --- Event subscriptions ---
            sapi.Event.PlayerJoin += OnPlayerJoin;
            sapi.Event.PlayerLeave += OnPlayerLeave;
            tickListenerId = sapi.Event.RegisterGameTickListener(OnGameTick, 1000);

            // --- Network channel for the HUD timer ---
            serverChannel = sapi.Network.RegisterChannel("warwthtreason")
                .RegisterMessageType<CombatTimerPacket>();

            // --- Harmony ---
            harmony = new Harmony(Mod.Info.ModID);
            harmony.PatchAll();
            CombatLogSystemAccessor.Instance = this;

            // --- /claimflag command ---
            RegisterClaimFlagCommands();

            sapi.Logger.Notification("[WarWithoutReason] Server-side initialization complete.");
        }

        private void RegisterClaimFlagCommands()
        {
            sapi.ChatCommands.Create("claimflag")
                .WithDescription(Lang.Get("warwthtreason:cmd-claimflag-desc"))
                .BeginSubCommand("pvp")
                    .WithDescription(Lang.Get("warwthtreason:cmd-claimflag-pvp-desc"))
                    .WithArgs(sapi.ChatCommands.Parsers.Word("value"))
                    .HandleWith(args => ToggleClaimFlag(args, isPvP: true))
                .EndSubCommand()
                .BeginSubCommand("pve")
                    .WithDescription(Lang.Get("warwthtreason:cmd-claimflag-pve-desc"))
                    .WithArgs(sapi.ChatCommands.Parsers.Word("value"))
                    .HandleWith(args => ToggleClaimFlag(args, isPvP: false))
                .EndSubCommand()
                .BeginSubCommand("status")
                    .WithDescription(Lang.Get("warwthtreason:cmd-claimflag-status-desc"))
                    .HandleWith(OnShowClaimStatus)
                .EndSubCommand();
        }

        // === Player events ===

        private void OnPlayerJoin(IServerPlayer player)
        {
            // Explicit null-check narrows 'player' to non-null for the rest of the method.
            // This silences the CS8602 warnings that appear if we use 'player?.Entity' below.
            if (player == null) return;

            var entity = player.Entity;
            if (entity == null) return;

            var healthBehavior = entity.GetBehavior<EntityBehaviorHealth>();
            if (healthBehavior == null) return;

            // Store the delegate so we can unsubscribe on leave.
            // This prevents duplicate handlers if the player rejoins quickly.
            OnDamagedDelegate handler = (damage, damageSource) => OnPlayerDamaged(player, damage, damageSource);
            damageHandlers[player.PlayerUID] = handler;
            healthBehavior.onDamaged += handler;
        }

        private void OnPlayerLeave(IServerPlayer player)
        {
            if (player == null) return;

            // Unsubscribe from onDamaged before the entity is destroyed.
            // This ensures we don't leak handlers.
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

            // Existing combat-logout logic.
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
                        $"logged out during combat. Punishment skipped (KillOnCombatLogout = false).");
                }
            }
        }

        // === Combat log ===

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

        // === Tick & timers ===

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

        // === Combat logging punishment ===

        private void KillCombatLogger(IServerPlayer player)
        {
            // Explicit null-check narrows 'player' to non-null for the rest of the method.
            // This silences CS8602 on the player.PlayerName / player.PlayerUID lines below.
            if (player == null) return;

            var entity = player.Entity;
            if (entity == null || !entity.Alive) return;

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
                $"logged out during combat and was killed.");

            foreach (var onlinePlayer in sapi.World.AllOnlinePlayers)
            {
                if (onlinePlayer is IServerPlayer sp)
                {
                    string message = Lang.GetL(sp.LanguageCode, "warwthtreason:combat-log-kill", player.PlayerName ?? "unknown");
                    sp.SendMessage(GlobalConstants.GeneralChatGroup, message, EnumChatType.Notification);
                }
            }
        }

        // === Public helpers for patches ===

        public bool IsInCombat(IServerPlayer player) => combatTimers.ContainsKey(player.PlayerUID);

        public LandClaim[]? GetClaimsAt(BlockPos pos) => sapi.World.Claims.Get(pos);

        // Returns a unique string ID for a claim.
        // Uses center coordinates because LandClaim has no public Index/Id,
        // and claims cannot overlap, so centers are guaranteed unique.
        public string GetClaimId(LandClaim claim)
        {
            var center = claim.Center;
            return $"{center.X},{center.Y},{center.Z}";
        }

        public ClaimFlags? GetClaimFlags(LandClaim claim)
        {
            if (claim == null) return null;
            string id = GetClaimId(claim);
            return claimFlagsConfig.Flags.TryGetValue(id, out var flags) ? flags : null;
        }

        // === /claimflag handlers ===

        private TextCommandResult ToggleClaimFlag(TextCommandCallingArgs args, bool isPvP)
        {
            if (args.Caller.Player is not IServerPlayer player)
                return TextCommandResult.Error(Lang.Get("warwthtreason:err-players-only"));

            var claims = sapi.World.Claims.Get(player.Entity.Pos.AsBlockPos);
            if (claims == null || claims.Length == 0)
                return TextCommandResult.Error(Lang.GetL(player.LanguageCode, "warwthtreason:err-not-in-claim"));

            var claim = claims[0];

            bool hasAccess = sapi.World.Claims.TryAccess(
                player, player.Entity.Pos.AsBlockPos, EnumBlockAccessFlags.BuildOrBreak);

            if (!hasAccess)
                return TextCommandResult.Error(Lang.GetL(player.LanguageCode, "warwthtreason:err-no-permission"));

            string claimId = GetClaimId(claim);
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
                    return TextCommandResult.Error(Lang.GetL(player.LanguageCode, "warwthtreason:err-invalid-value"));
            }

            if (isPvP) flags.AllowPvP = newValue;
            else flags.AllowPvE = newValue;

            sapi.StoreModConfig(claimFlagsConfig, "WWR_ClaimFlags.json");

            string flagName = isPvP ? "PvP" : "PvE";

            if (value == "default")
            {
                return TextCommandResult.Success(
                    Lang.GetL(player.LanguageCode, "warwthtreason:flag-reset", flagName, claimId));
            }

            return TextCommandResult.Success(
                Lang.GetL(player.LanguageCode, "warwthtreason:flag-set", flagName, claimId, value));
        }

        private TextCommandResult OnShowClaimStatus(TextCommandCallingArgs args)
        {
            if (args.Caller.Player is not IServerPlayer player)
                return TextCommandResult.Error(Lang.Get("warwthtreason:err-players-only"));

            var claims = sapi.World.Claims.Get(player.Entity.Pos.AsBlockPos);
            if (claims == null || claims.Length == 0)
                return TextCommandResult.Error(Lang.GetL(player.LanguageCode, "warwthtreason:err-not-in-claim"));

            string claimId = GetClaimId(claims[0]);

            if (!claimFlagsConfig.Flags.TryGetValue(claimId, out var flags))
            {
                return TextCommandResult.Success(
                    Lang.GetL(player.LanguageCode, "warwthtreason:claim-status-empty", claimId));
            }

            string defaultValue = Lang.GetL(player.LanguageCode, "warwthtreason:value-default");
            string pvp = flags.AllowPvP?.ToString() ?? defaultValue;
            string pve = flags.AllowPvE?.ToString() ?? defaultValue;

            return TextCommandResult.Success(
                Lang.GetL(player.LanguageCode, "warwthtreason:claim-status-full", claimId, pvp, pve));
        }

        // === Shutdown ===

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

            sapi?.Logger.Notification("[WarWithoutReason] Server-side shutdown complete.");
        }
    }
}