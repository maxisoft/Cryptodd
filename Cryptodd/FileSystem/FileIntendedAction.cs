namespace Cryptodd.FileSystem;

[Flags]
public enum FileIntendedAction
{
    None = 0,
    Read = 1,
    Write = 1 << 1,
    Create = 1 << 2,
    Delete = 1 << 3,
    Append = 1 << 4
}