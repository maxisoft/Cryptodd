namespace Cryptodd.Ftx.Models;

public struct ResponseEnvelope<T>
{
    public bool Success { get; set; }
    public T? Result { get; set; }
}