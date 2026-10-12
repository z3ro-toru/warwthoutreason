using HarmonyLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

using warwthtreason.Common;
using warwthtreason.Server.Compatibility;
using warwthtreason.Server.DeadHand;
using warwthtreason.Server.PlayerStats;


namespace warwthtreason.Server
{
    // Static accessor used by Harmony patches in other files.
    public static class CombatLogSystemAccessor
    {
        public static CombatLogSystem? Instance { get; set; }
    }

    // Main entry point of the mod. This file is responsible for orchestration:
    // initialization order, event wiring, network setup, and shutdown.
    // Player handling and claim flag logic live in sibling partial files.
    public partial class CombatLogSystem : ModSystem
    {
        // === Core references ===
        private ICoreServerAPI sapi = null!;
        private CombatLogConfig config = null!;
        private ClaimFlagsConfig claimFlagsConfig = null!;
        private DeadHandManager deadHand = null!;
        private PlayerStatsManager playerStats = null!;
        

        // === Combat state ===
        // Key: PlayerUID. Value: combat end time (ElapsedMilliseconds + duration).
        private readonly Dictionary<string, long> combatTimers = [];

        // Stores delegates subscribed to onDamaged for subsequent unsubscription.
        private readonly Dictionary<string, OnDamagedDelegate> damageHandlers = [];

        // === Infrastructure ===
        private long tickListenerId;
        private Harmony? harmony;
        private IServerNetworkChannel? serverChannel;

        // === Public accessors for patches and the client ===
        public CombatLogConfig Config => config;
        public ICoreServerAPI Sapi => sapi;
        // True if the player is currently flagged as in combat.
        public bool IsInCombat(IServerPlayer player) => combatTimers.ContainsKey(player.PlayerUID);

        public override void StartServerSide(ICoreServerAPI api)
        {
            sapi = api;

            // Ordered initialization. Each step is isolated so failures can be
            // diagnosed from the server log, and new steps can be slotted in
            // without touching the orchestration code.
            InitializeConfigs();
            CheckModCompatibility();
            RegisterEventHandlers();
            InitializeNetwork();
            InitializeHarmony();
            RegisterClaimFlagCommands();

            sapi.Logger.Notification("[WarWithoutReason] Server-side initialization complete.");
        }

        // --- Step 1: Configs ---
        public const string ConfigDir = "WarWithoutReason";

        private void InitializeConfigs()
        {
            // Migrate legacy config files from ModConfig root into the new subfolder.
            MigrateConfigIfNeeded(Constants.ConfigMain, $"{Constants.ConfigDir}/{Constants.ConfigMain}");
            MigrateConfigIfNeeded(Constants.ConfigClaimFlags, $"{Constants.ConfigDir}/{Constants.ConfigClaimFlags}");
            MigrateConfigIfNeeded(Constants.ConfigPending, $"{Constants.ConfigDir}/{Constants.ConfigPending}");

            config = sapi.LoadModConfig<CombatLogConfig>($"{Constants.ConfigDir}/{Constants.ConfigMain}");
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
            sapi.StoreModConfig(config, $"{Constants.ConfigDir}/{Constants.ConfigMain}");

            claimFlagsConfig = sapi.LoadModConfig<ClaimFlagsConfig>($"{Constants.ConfigDir}/{Constants.ConfigClaimFlags}");
            if (claimFlagsConfig == null)
            {
                claimFlagsConfig = new ClaimFlagsConfig();
                sapi.StoreModConfig(claimFlagsConfig, $"{Constants.ConfigDir}/{Constants.ConfigClaimFlags}");
            }

            playerStats = new PlayerStatsManager(sapi);

            deadHand = new DeadHandManager(sapi, config, RestoreCombatTimer);
        }

        // Moves a legacy config file from ModConfig root into the new subfolder.
        // No-op if the old file doesn't exist or the new one already does.
        private void MigrateConfigIfNeeded(string oldRelativePath, string newRelativePath)
        {
            string basePath = sapi.GetOrCreateDataPath("ModConfig");
            string oldFull = Path.Combine(basePath, oldRelativePath);
            string newFull = Path.Combine(basePath, newRelativePath);

            if (!File.Exists(oldFull)) return;
            if (File.Exists(newFull)) return;

            Directory.CreateDirectory(Path.GetDirectoryName(newFull)!);
            File.Move(oldFull, newFull);
            sapi.Logger.Notification($"[WarWithoutReason] Migrated config: {oldRelativePath} → {newRelativePath}");
        }

        // --- Step 2: Optional mod detection ---
        // Logs the presence of overlapping claim-protection mods, but does NOT
        // alter the config. Both mods can coexist: they hook different methods
        // (SafeZone/DPVPIC on Entity.ReceiveDamage, we on ShouldReceiveDamage),
        // so they don't conflict. Whether WWR protection should stay enabled
        // is the server admin's decision, not the mod's.
        private void CheckModCompatibility()
        {
            if (sapi.ModLoader.IsModEnabled("safezone"))
            {
                sapi.Logger.Notification("[WarWithoutReason] SafeZone detected. Both mods will run in parallel — SafeZone handles Entity.ReceiveDamage, WWR handles ShouldReceiveDamage.");
            }

            if (sapi.ModLoader.IsModEnabled("disablepvpinsideclaims"))
            {
                sapi.Logger.Notification("[WarWithoutReason] DisablePvPInsideClaims detected. Both mods will run in parallel — DPVPIC handles Entity.ReceiveDamage, WWR handles ShouldReceiveDamage.");
            }

            if (!sapi.ModLoader.IsModEnabled("safezone") && !sapi.ModLoader.IsModEnabled("disablepvpinsideclaims"))
            {
                sapi.Logger.Notification("[WarWithoutReason] No conflicting claim-protection mod found. Built-in protection enabled.");
            }
        }

        // --- Step 3: Event subscriptions ---
        private void RegisterEventHandlers()
        {
            sapi.Event.PlayerJoin += OnPlayerJoin;
            sapi.Event.PlayerLeave += OnPlayerLeave;
            sapi.Event.OnEntityDeath += OnEntityDeath;
            tickListenerId = sapi.Event.RegisterGameTickListener(OnGameTick, 1000);
        }

        // --- Step 4: Network channel ---
        private void InitializeNetwork()
        {
            serverChannel = sapi.Network.RegisterChannel(Constants.NetworkChannel)
                .RegisterMessageType<CombatTimerPacket>();
        }

        // --- Step 5: Harmony patches ---
        private void InitializeHarmony()
        {
            harmony = new Harmony(Constants.HarmonyId);
            CombatLogSystemAccessor.Instance = this;

            SafeZoneCombatOverridePatch.Sapi = sapi;
            DisablePvPInsideClaimsCombatPatch.Sapi = sapi;

            sapi.Event.ServerRunPhase(EnumServerRunPhase.ModsAndConfigReady, () =>
            {
                harmony.PatchAll();
                sapi.Logger.Notification("[WarWithoutReason] Harmony patches applied.");

#if DEBUG
                DumpPatches("Entity.ReceiveDamage",
                    AccessTools.Method(typeof(Entity), nameof(Entity.ReceiveDamage)));

                DumpPatches("SafeZone.Patch_Entity_ReceiveDamage.Prefix",
                    AccessTools.Method("SafeZone.Patch_Entity_ReceiveDamage:Prefix"));

                DumpPatches("EntityBehaviorHealth.OnEntityReceiveDamage",
                    AccessTools.Method(typeof(EntityBehaviorHealth), "OnEntityReceiveDamage"));

                DumpPatches("EntityPlayer.ShouldReceiveDamage",
                    AccessTools.Method(typeof(EntityPlayer), nameof(EntityPlayer.ShouldReceiveDamage)));
#endif
            });
                        
#if DEBUG
            // Local helper — dumps every Harmony patch on a method.
            // Exists only in Debug builds; in Release it's compiled out entirely.
            void DumpPatches(string label, MethodBase? method)
            {
                if (method == null)
                {
                    sapi.Logger.Notification($"[WWR DEBUG] {label}: method not found.");
                    return;
                }

                var info = Harmony.GetPatchInfo(method);
                if (info == null)
                {
                    sapi.Logger.Notification($"[WWR DEBUG] {label}: no patches.");
                    return;
                }

                foreach (var p in info.Prefixes)
                    sapi.Logger.Notification($"[WWR DEBUG] {label} — Prefix owner={p.owner}, method={p.PatchMethod.DeclaringType?.Name}.{p.PatchMethod.Name}");
                foreach (var p in info.Postfixes)
                    sapi.Logger.Notification($"[WWR DEBUG] {label} — Postfix owner={p.owner}, method={p.PatchMethod.DeclaringType?.Name}.{p.PatchMethod.Name}");
                foreach (var p in info.Transpilers)
                    sapi.Logger.Notification($"[WWR DEBUG] {label} — Transpiler owner={p.owner}, method={p.PatchMethod.DeclaringType?.Name}.{p.PatchMethod.Name}");
            }
#endif
        }

        // === Shutdown ===

        public override void Dispose()
        {
            if (sapi != null)
            {
                sapi.Event.PlayerJoin -= OnPlayerJoin;
                sapi.Event.PlayerLeave -= OnPlayerLeave;
                sapi.Event.OnEntityDeath -= OnEntityDeath;
                playerStats?.Flush();
                sapi.Event.UnregisterGameTickListener(tickListenerId);
            }

            harmony?.UnpatchAll(Constants.HarmonyId);
            CombatLogSystemAccessor.Instance = null;

            sapi?.Logger.Notification("[WarWithoutReason] Server-side shutdown complete.");
        }
    }
}