using Newtonsoft.Json;

namespace warwthtreason.Server
{
    // Класс конфигурации. Поля публичные, чтобы Newtonsoft.Json мог их сериализовать.
    public class CombatLogConfig
    {
        // Длительность режима боя в секундах. По умолчанию 30.
        public int CombatDurationSeconds { get; set; } = 30;

        // Включает/выключает сообщения в текстовом чате игрока.
        // true — сообщения будут отправляться, false — нет.
        public bool SendChatMessages { get; set; } = true;

        // Включает/выключает крупные уведомления по центру экрана/HUD.
        // true — уведомления будут показываться, false — нет.
        public bool ShowScreenMessages { get; set; } = true;
    }
}