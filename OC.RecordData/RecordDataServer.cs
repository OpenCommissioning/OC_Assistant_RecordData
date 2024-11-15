using OC.Assistant.Sdk.Plugin;

namespace OC.RecordData;

public class RecordDataServer : PluginBase
{
    [PluginParameter("ADS Port\nDefault 852")]
    private readonly ushort _port = 852;

    private AdsServer? _adsServer;

    protected override bool OnSave()
    {
        return true;
    }

    protected override bool OnStart()
    {
        _adsServer = new AdsServer(_port);
        return true;
    }

    protected override void OnUpdate()
    {
    }

    protected override void OnStop()
    {
        _adsServer?.Disconnect();
    }
}