using System.Collections.Concurrent;
using System.Diagnostics;
using OC.Assistant.Sdk;
using TwinCAT.Ads;

namespace OC.RecordData;

internal class AdsServer : TwinCAT.Ads.Server.AdsServer
{
    private readonly AmsAddress _plcAddress = new (851);
    private readonly ConcurrentQueue<Telegram> _writeInd = new();
    private readonly ConcurrentQueue<Telegram> _readInd = new();
    private readonly ConcurrentDictionary<uint, Origin> _writeRes = new();
    private readonly ConcurrentDictionary<uint, Origin> _readRes = new();
    private readonly TcRecordDataList _tcRecordDataList = new();
        
    /// <summary>
    /// Custom <see cref="TwinCAT.Ads.Server.AdsServer"/> to catch and forward read- and write-requests.
    /// </summary>
    public AdsServer(ushort port) : base(port, "Open Commissioning AdsServer for RecordData")
    {
        base.ConnectServer();
        Task.Run(Update);
        OC.Assistant.Sdk.Logger.LogInfo(this, $"AdsServer {AmsServer.ServerAddress?.NetId}:{AmsServer.ServerAddress?.Port} connected and started");
    }

    /// <summary>
    /// Catches write indication and stores to fifo.
    /// <inheritdoc/>
    /// </summary>
    /// <inheritdoc/>
    protected override Task<ResultWrite> OnWriteAsync(AmsAddress amsAddress, uint invokeId, uint indexGroup, uint indexOffset, ReadOnlyMemory<byte> data, CancellationToken cancel)
    {
        return Task.Run(() =>
        {
            var iGrp = indexGroup & 0x8000ffff;
            var telegram = new Telegram(amsAddress, invokeId, iGrp, indexOffset, (uint)data.Length, data.ToArray());
            
            if (!_tcRecordDataList.Contains(telegram.Key))
            {
                ResultWrite.CreateError(AdsErrorCode.DeviceServiceNotSupported, invokeId);
            }
            
            _writeInd.Enqueue(telegram);
            OC.Assistant.Sdk.Logger.LogInfo(this, $"AdsWriteInd from {amsAddress.NetId}:{amsAddress.Port} IGrp {iGrp:X} IOffs {indexOffset:X}", true);
            return ResultWrite.CreateError(AdsErrorCode.NoError, invokeId);
        }, cancel);
    }
        
    /// <summary>
    /// Catches read indication and stores to fifo.
    /// <inheritdoc />
    /// </summary>
    /// <inheritdoc />
    protected override Task<ResultReadBytes> OnReadAsync(AmsAddress rAddr, uint invokeId, uint indexGroup, uint indexOffset, int cbLength, CancellationToken cancel)
    {
        return Task.Run(() =>
        {
            var iGrp = indexGroup & 0x8000ffff;
            var telegram = new Telegram(rAddr, invokeId, iGrp, indexOffset, (uint)cbLength, null);
            
            if (!_tcRecordDataList.Contains(telegram.Key))
            {
                ResultReadBytes.CreateError(AdsErrorCode.DeviceServiceNotSupported, invokeId);
            }
            
            _readInd.Enqueue(telegram);
            OC.Assistant.Sdk.Logger.LogInfo(this, $"AdsReadInd from {rAddr.NetId}:{rAddr.Port} IGrp {iGrp:X} IOffs {indexOffset:X}", true);
            return ResultReadBytes.CreateError(AdsErrorCode.NoError, invokeId);
        }, cancel);
    }
    
    /// <summary>
    /// Catches write response, removes matched telegram from dictionary and forwards to origin.
    /// <inheritdoc/>
    /// </summary>
    /// <inheritdoc/>
    protected override Task<AdsErrorCode> OnWriteConfirmationAsync(AmsAddress rAddr, uint invokeId, AdsErrorCode result, CancellationToken cancel)
    {
        return Task.Run(() =>
        {
            //Check if appropriate telegram has been stored
            if (!_writeRes.TryRemove(invokeId, out var origin))
            {
                return AdsErrorCode.DeviceServiceNotSupported;
            }
            
            //Send response to origin
            WriteResponseAsync(origin.AmsAddress, origin.InvokeId, result, cancel);
            OC.Assistant.Sdk.Logger.LogInfo(this, $"AdsWriteRes to {origin.AmsAddress.NetId}:{origin.AmsAddress.Port}", true);
        
            return AdsErrorCode.NoError;
        }, cancel);
    }
    
    
    /// <summary>
    /// Catches read response, removes matched telegram from dictionary and forwards to origin.
    /// <inheritdoc/>
    /// </summary>
    /// <inheritdoc/>
    protected override Task<AdsErrorCode> OnReadConfirmationAsync(AmsAddress rAddr, uint invokeId, AdsErrorCode result, ReadOnlyMemory<byte> data, CancellationToken cancel)
    {
        return Task.Run(() =>
        {
            //Check if appropriate telegram has been stored
            if (!_readRes.TryRemove(invokeId, out var origin))
            {
                return AdsErrorCode.DeviceServiceNotSupported;
            }

            //Send response to origin
            ReadResponseAsync(origin.AmsAddress, origin.InvokeId, result, data, cancel);
            OC.Assistant.Sdk.Logger.LogInfo(this, $"AdsReadRes to {origin.AmsAddress.NetId}:{origin.AmsAddress.Port}", true);
        
            return AdsErrorCode.NoError;
        }, cancel);
    }

    /// <summary>
    /// Sends stored read and write requests to the Plc.
    /// </summary>
    private void Update()
    {
        var stopwatch = new Stopwatch();
                
        while (IsConnected)
        {
            stopwatch.WaitUntil(10);
                
            try
            {
                if (_writeInd.TryDequeue(out var telegram))
                {
                    var key = telegram.Key;
                        
                    //Store origin to the write result dictionary
                    _writeRes.TryAdd(key, telegram.Origin);
                        
                    //Send telegram to Plc, use key as invokeId
                    WriteRequest(
                        _plcAddress, key,
                        telegram.IndexGroup,
                        telegram.IndexOffset,
                        new ReadOnlySpan<byte>(telegram.Data, 0, (int)telegram.Length));
                }
            }
            catch(Exception e)
            {
                OC.Assistant.Sdk.Logger.LogError(this, $"AdsServer {AmsServer.ServerAddress?.NetId}:{AmsServer.ServerAddress?.Port} error: {e.Message}", true);
            }
                
            try
            {
                if (_readInd.TryDequeue(out var telegram))
                {
                    var key = telegram.Key;
                        
                    //Store origin to the read result dictionary
                    _readRes.TryAdd(key, telegram.Origin);
                        
                    //Send telegram to Plc, use key as invokeId
                    ReadRequest(
                        _plcAddress, 
                        key, 
                        telegram.IndexGroup, 
                        telegram.IndexOffset,
                        (int)telegram.Length);
                }
            }
            catch(Exception e)
            {
                OC.Assistant.Sdk.Logger.LogError(this, $"AdsServer {AmsServer.ServerAddress?.NetId}:{AmsServer.ServerAddress?.Port} error: {e.Message}", true);
            }
        }
            
        OC.Assistant.Sdk.Logger.LogInfo(this, $"AdsServer {AmsServer.ServerAddress?.NetId}:{AmsServer.ServerAddress?.Port} disconnected and stopped");
    }
}