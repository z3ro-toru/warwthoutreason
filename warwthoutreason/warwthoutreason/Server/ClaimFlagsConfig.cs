using System.Collections.Generic;

namespace warwthtreason.Server
{
    // Конфиг для per-claim флагов. Сериализуется в ModConfig/ClaimFlags.json.
    public class ClaimFlagsConfig
    {
        // Ключ — уникальный ID привата (генерируется из координат углов).
        // Значение — набор флагов для этого привата.
        public Dictionary<string, ClaimFlags> Flags { get; set; } = new Dictionary<string, ClaimFlags>();
    }

    // Набор флагов одного привата.
    public class ClaimFlags
    {
        // null — используется глобальная настройка PreventPvPInClaims.
        // true — PvP разрешён в этом привате.
        // false — PvP запрещён в этом привате.
        public bool? AllowPvP { get; set; } = null;

        // Аналогично для PvE.
        public bool? AllowPvE { get; set; } = null;
    }
}