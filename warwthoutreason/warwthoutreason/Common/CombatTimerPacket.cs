using ProtoBuf;

namespace warwthtreason.Common
{
    // Пакет передаёт клиенту, сколько секунд осталось до выхода из боя.
    // Атрибут [ProtoContract] обязателен — он помечает класс как сериализуемый ProtoBuf.
    [ProtoContract]
    public class CombatTimerPacket
    {
        // [ProtoMember(1)] — порядковый номер поля. Не меняйте номера существующих полей,
        // иначе клиент и сервер перестанут понимать друг друга.
        [ProtoMember(1)]
        public int RemainingSeconds { get; set; }
    }
}