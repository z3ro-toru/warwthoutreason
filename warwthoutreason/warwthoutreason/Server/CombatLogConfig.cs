namespace warwthtreason.Server
{
    // Основной конфиг мода. Сериализуется в ModConfig/WarWithoutReason.json.
    public class CombatLogConfig
    {
        // Длительность режима боя в секундах.
        public int CombatDurationSeconds { get; set; } = 30;

        // Отправлять ли сообщения в чат при входе/выходе из боя.
        public bool SendChatMessages { get; set; } = true;

        // Показывать ли крупные уведомления на экране.
        public bool ShowScreenMessages { get; set; } = true;

        // Убивать ли игрока, вышедшего из игры во время боя.
        public bool KillOnCombatLogout { get; set; } = true;

        // Включает собственную систему защиты приватов.
        // Автоматически отключается при обнаружении SafeZone.
        public bool EnableClaimProtection { get; set; } = true;

        // Глобальный запрет PvP в приватах.
        // Переопределяется per-claim флагом AllowPvP.
        public bool PreventPvPInClaims { get; set; } = true;

        // Глобальный запрет PvE (урон от мобов) в приватах.
        // По умолчанию выключен, чтобы мобы атаковали как обычно.
        public bool PreventPvEInClaims { get; set; } = false;
    }
}