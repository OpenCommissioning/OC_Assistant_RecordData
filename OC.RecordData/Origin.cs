using TwinCAT.Ads;

namespace OC.RecordData;

internal readonly struct Origin(AmsAddress amsAddress, uint invokeId)
{
    public AmsAddress AmsAddress { get; } = amsAddress;
    public uint InvokeId { get; } = invokeId;
}