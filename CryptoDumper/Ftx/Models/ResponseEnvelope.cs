namespace CryptoDumper.Ftx.Models;

public struct ResponseEnvelope<T>
{
    public bool Success { get; set; }
    public T? Result { get; set; }

    public override string ToString() => $"{nameof(Success)}: {Success}, {nameof(Result)}: {Result}";
}