using ProtoBuf;

namespace warwthtreason.Common
{
    // Пакет передаёт клиенту, сколько секунд осталось до выхода из боя.
    // Атрибут [ProtoContract] обязателен — он помечает класс как сериализуемый.
    [ProtoContract]
    public class CombatTimerPacket
    {
        // [ProtoMember(1)] — порядковый номер поля при сериализации.
        // Если вы добавите новые поля, нумеруйте их 2, 3, 4 и т.д.,
        // не меняя уже существующие номера (иначе сломается совместимость).
        [ProtoMember(1)]
        public int RemainingSeconds { get; set; }
    }
}