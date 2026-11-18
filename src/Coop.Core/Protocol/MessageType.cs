namespace Coop.Core.Protocol
{
    public enum MessageType : byte
    {
        Hello = 1,
        Welcome = 2,
        Reject = 3,
        PlayerState = 4,
        Ping = 5,
        Pong = 6,
        Bye = 7,
        SpriteDef = 8,
        FloorLayout = 9,
        DungeonTravel = 10,
        TownTravel = 11,
        RoomChange = 12,
        EnemyDamage = 13,
        EnemyHealth = 14,
        GoldDelta = 15,
        GoldTotal = 16,
        Revive = 17,
        DayTimeSync = 18,
        PoolSpent = 19,
        BagSnapshot = 20,
        EmblemReturn = 21,
    }
}
