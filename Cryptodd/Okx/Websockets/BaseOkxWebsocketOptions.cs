using Cryptodd.Websockets;

namespace Cryptodd.Okx.Websockets;

public abstract class BaseOkxWebsocketOptions : BaseWebsocketOptions
{
    public const string DefaultAddress = "wss://wsaws.okx.com:8443/ws/v5/public";

    protected BaseOkxWebsocketOptions()
    {
        if (string.IsNullOrEmpty(BaseAddress))
        {
            BaseAddress = DefaultAddress;
        }
    }

    public int MaxStreamCountSoftLimit { get; set; } = 128;

    public int SubscribeMaxBytesLength { get; set; } = 4096; 

    public TimeSpan PingInterval { get; set; } = TimeSpan.FromSeconds(10);
    
    public bool? CloseOnInvalidSubscription { get; set; }
    
    public bool? CloseOnErrorMessage { get; set; }
}