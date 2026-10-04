using System;
using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Server;

namespace CombatLogMod
{
    // Патч, который переопределяет поведение SafeZone при активном режиме боя.
    // Наша цель — не сам Entity.ReceiveDamage, а патч-метод SafeZone,
    // чтобы полностью "отключить" его, когда игроки в бою.
    [HarmonyPatch]
    public static class SafeZoneCombatOverridePatch
    {
        // Метод TargetMethod возвращает MethodBase, который нужно пропатчить.
        // Harmony вызывает его при применении патча, чтобы узнать цель.
        // Здесь мы ищем SafeZone.Patch_Entity_ReceiveDamage.Prefix в загруженных сборках.
        static MethodBase? TargetMethod()
        {
            // Перебираем все загруженные сборки — SafeZone может быть в любой из них.
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                // Ищем тип по полному имени (namespace.class).
                // Если найдём — это и есть цель.
                var patchType = asm.GetType("SafeZone.Patch_Entity_ReceiveDamage");
                if (patchType != null)
                {
                    // Возвращаем метод Prefix этого класса.
                    return AccessTools.Method(patchType, "Prefix");
                }
            }

            // Если SafeZone не установлен — возвращаем null.
            // Harmony пропустит патч без ошибки.
            return null;
        }

        // Наш Prefix выполняется ПЕРЕД Prefix'ом SafeZone.
        // Возврат false — пропустить SafeZone's Prefix (значит, SafeZone НЕ заблокирует урон).
        // Возврат true  — дать SafeZone's Prefix выполниться (он сам решит, блокировать или нет).
        // __0 = первый аргумент целевого метода (Entity, получатель урона).
        // __1 = второй аргумент (DamageSource).
        static bool Prefix(Entity __0, DamageSource __1)
        {
            // Если наша система не инициализирована — не вмешиваемся.
            var system = CombatLogSystemAccessor.Instance;
            if (system == null) return true;

            // Получаем жертву — это первый аргумент метода Prefix SafeZone.
            var victimEntity = __0 as EntityPlayer;
            if (victimEntity?.Player is not IServerPlayer victim) return true;

            var damageSource = __1;

            // Нас интересует только PvP-урон от игрока.
            if (damageSource.Source != EnumDamageSource.Player) return true;

            // Получаем атакующего.
            var attackerEntity = damageSource.GetCauseEntity() as EntityPlayer;
            if (attackerEntity?.Player is not IServerPlayer attacker) return true;

            // Игнорируем self-damage.
            if (attacker == victim) return true;

            // --- КЛЮЧЕВАЯ ПРОВЕРКА ---
            // Если хотя бы один из участников в бою — пропускаем SafeZone's Prefix,
            // возвращая false. Это значит, что SafeZone не сможет заблокировать урон.
            if (system.IsInCombat(attacker) || system.IsInCombat(victim))
            {
                return false; // Skip SafeZone's Prefix entirely
            }

            // Если никто не в бою — пусть SafeZone работает как обычно.
            // Он сам решит, блокировать урон или нет.
            return true;
        }
    }
}