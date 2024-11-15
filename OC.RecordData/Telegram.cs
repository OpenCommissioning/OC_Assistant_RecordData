using TwinCAT.Ads;

namespace OC.RecordData;

internal readonly struct Telegram(
    AmsAddress amsAddress,
    uint invokeId,
    uint indexGroup,
    uint indexOffset,
    uint length,
    byte[]? data)
{
    public Origin Origin { get; } = new(amsAddress, invokeId);
    public uint IndexGroup { get; } = indexGroup;
    public uint IndexOffset { get; } = indexOffset;
    public uint Length { get; } = length;
    public byte[]? Data { get; } = data;

    /// <summary>
    /// 0x DD CC BB AA where<br/>
    /// AA = Port low byte<br/>
    /// BB = Port high byte<br/>
    /// CC = SubSlot<br/>
    /// DD = Slot<br/>
    /// </summary>
    public uint Key
    {
        get
        {
            var subSlot = (byte)IndexOffset;
            var slot = (byte)(IndexOffset >> 16);
            var port = (ushort) Origin.AmsAddress.Port;
            return port + (uint)(subSlot * 0x10000) + (uint)(slot * 0x1000000);
        }
    }
}