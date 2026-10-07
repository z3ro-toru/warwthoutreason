using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace warwthtreason.Client
{
    public class CombatTimerHud : HudElement
    {
        private readonly GuiElementDynamicText? timerText;

        public CombatTimerHud(ICoreClientAPI capi) : base(capi)
        {
            // A compositor is a container for HUD elements. It's anchored to the top center of the screen.
            var composer = capi.Gui.CreateCompo("combattimerhud",
                ElementBounds.Fixed(EnumDialogArea.CenterTop, 0, 80, 400, 50));

            composer.AddDynamicText(
                "",
                CairoFont.WhiteDetailText().WithFontSize(24),
                ElementBounds.Fixed(0, 0, 400, 50),
                "combattimer");

            SingleComposer = composer.Compose();
            timerText = SingleComposer.GetDynamicText("combattimer");
        }

        // Updates the HUD text. An empty string = the element is not displayed visually.
        public void SetTime(int seconds)
        {
            if (timerText == null) return;

            if (seconds <= 0)
            {
                timerText.SetNewText("");
            }
            else
            {
                timerText.SetNewText(Lang.Get("warwthtreason:combat-timer", seconds));
            }
        }
    }
}