using Vintagestory.API.Client;
using Vintagestory.API.Common;

using warwthtreason.Common;


namespace warwthtreason.Client
{
    public class CombatLogClientSystem : ModSystem
    {
        private ICoreClientAPI capi = null!;
        private CombatTimerHud? hud;

        public override void StartClientSide(ICoreClientAPI api)
        {
            capi = api;

            // The channel name MUST match the server, character-for-character.
            api.Network.RegisterChannel(Constants.NetworkChannel)
                .RegisterMessageType<CombatTimerPacket>()
                .SetMessageHandler<CombatTimerPacket>(OnTimerPacket);

            hud = new CombatTimerHud(capi);
            capi.Gui.RegisterDialog(hud);
        }

        private void OnTimerPacket(CombatTimerPacket packet)
        {
            capi.Logger.Notification($"[WWR DEBUG] Received timer: {packet.RemainingSeconds}s");
            hud?.SetTime(packet.RemainingSeconds);
        }
    }
}