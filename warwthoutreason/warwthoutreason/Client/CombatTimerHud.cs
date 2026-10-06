using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace warwthtreason.Client
{
    public class CombatTimerHud : HudElement
    {
        private GuiElementDynamicText? timerText;

        public CombatTimerHud(ICoreClientAPI capi) : base(capi)
        {
            // Композер — контейнер для элементов HUD. Привязка: верхний центр экрана.
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

        // Обновляет текст HUD. Пустая строка = элемент не отображается визуально.
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