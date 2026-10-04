using Vintagestory.API.Client;
using Vintagestory.API.Common;
using warwthtreason.Common; // Доступ к CombatTimerPacket

namespace warwthtreason.Client
{
    public class CombatLogClientSystem : ModSystem
    {
        private ICoreClientAPI capi = null!;
        private CombatTimerHud? hud;

        public override void StartClientSide(ICoreClientAPI api)
        {
            capi = api;

            // Имя канала ДОЛЖНО совпадать с сервером ("warwthtreason").
            api.Network.RegisterChannel("warwthtreason")
                .RegisterMessageType<CombatTimerPacket>()
                .SetMessageHandler<CombatTimerPacket>(OnTimerPacket);

            hud = new CombatTimerHud(capi);
            capi.Gui.RegisterDialog(hud);
        }

        private void OnTimerPacket(CombatTimerPacket packet)
        {
            hud?.SetTime(packet.RemainingSeconds);
        }
    }
}