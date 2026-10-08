using Vintagestory.API.Client;
using Vintagestory.API.Config;

namespace warwthtreason.Client
{
    public class CombatTimerHud : HudElement
    {
        private readonly GuiElementDynamicText? timerText;

        public CombatTimerHud(ICoreClientAPI capi) : base(capi)
        {
            // Создаём композер с чёткими границами.
            // Используем EnumDialogArea.CenterTop для привязки к верхнему центру экрана.
            var composer = capi.Gui.CreateCompo("combattimerhud",
                ElementBounds.Fixed(EnumDialogArea.CenterTop, 0, 100, 300, 40));

            // Добавляем динамический текст.
            composer.AddDynamicText(
                "", // Изначально пустой
                CairoFont.WhiteDetailText().WithFontSize(22),
                ElementBounds.Fixed(0, 0, 300, 40),
                "combattimer");

            // Собираем композер.
            SingleComposer = composer.Compose();

            // Получаем доступ к текстовому элементу.
            timerText = SingleComposer.GetDynamicText("combattimer");

            // ВАЖНО: Без этого вызова HUD не будет отображаться.
            TryOpen();
        }

        // Обновляет текст на экране.
        public void SetTime(int seconds)
        {
            if (timerText == null) return;

            string text;
            if (seconds <= 0)
            {
                text = ""; // Пустая строка = HUD скрыт
            }
            else
            {
                text = Lang.Get("warwthtreason:combat-timer", seconds);
            }

            // Устанавливаем новый текст.
            timerText.SetNewText(text, autoHeight: false, forceRedraw: true);

            // Принудительно перерисовываем композер, чтобы изменения вступили в силу немедленно.
            SingleComposer?.ReCompose();
        }
    }
}