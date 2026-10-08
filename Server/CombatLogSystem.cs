using HarmonyLib;
using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using warwthtreason.Common;
using warwthtreason.Server.DeadHand;

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
        private void InitializeConfigs()
        {
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

            claimFlagsConfig = sapi.LoadModConfig<ClaimFlagsConfig>("WWR_ClaimFlags.json");
            if (claimFlagsConfig == null)
            {
                claimFlagsConfig = new ClaimFlagsConfig();
                sapi.StoreModConfig(claimFlagsConfig, "WWR_ClaimFlags.json");
            }

            // DeadHand is self-contained: it reads config on construction and
            // manages its own persistence in WWR_PendingDeaths.json.
            deadHand = new DeadHandManager(sapi, config);
        }

        // --- Step 2: Optional mod detection ---
        // When an overlapping claim-protection mod is present, the built-in
        // protection is disabled. Combat timer, HUD, and logout punishment
        // remain fully functional.
        private void CheckModCompatibility()
        {
            if (sapi.ModLoader.IsModEnabled("safezone"))
            {
                sapi.Logger.Notification("[WarWithoutReason] SafeZone detected. Built-in claim protection disabled.");
                config.EnableClaimProtection = false;
                sapi.StoreModConfig(config, "WarWithoutReason.json");
                return;
            }

            if (sapi.ModLoader.IsModEnabled("disablepvpinsideclaims"))
            {
                sapi.Logger.Notification("[WarWithoutReason] DisablePvPInsideClaims detected. Built-in claim protection disabled.");
                config.EnableClaimProtection = false;
                sapi.StoreModConfig(config, "WarWithoutReason.json");
                return;
            }

            sapi.Logger.Notification("[WarWithoutReason] No conflicting claim-protection mod found. Built-in protection enabled.");
        }

        // --- Step 3: Event subscriptions ---
        private void RegisterEventHandlers()
        {
            sapi.Event.PlayerJoin += OnPlayerJoin;
            sapi.Event.PlayerLeave += OnPlayerLeave;
            tickListenerId = sapi.Event.RegisterGameTickListener(OnGameTick, 1000);
        }

        // --- Step 4: Network channel ---
        private void InitializeNetwork()
        {
            serverChannel = sapi.Network.RegisterChannel("warwthtreason")
                .RegisterMessageType<CombatTimerPacket>();
        }

        // --- Step 5: Harmony patches ---
        private void InitializeHarmony()
        {
            harmony = new Harmony(Mod.Info.ModID);
            CombatLogSystemAccessor.Instance = this;

            // Delay PatchAll until all mods are loaded, so patches can resolve
            // types from other mods (SafeZone, DisablePvPInsideClaims).
            sapi.Event.ServerRunPhase(EnumServerRunPhase.ModsAndConfigReady, () =>
            {
                harmony.PatchAll();
                sapi.Logger.Notification("[WarWithoutReason] Harmony patches applied.");
            });
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