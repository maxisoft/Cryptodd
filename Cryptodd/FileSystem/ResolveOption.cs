namespace Cryptodd.FileSystem;

public struct ResolveOption : IEquatable<ResolveOption>
{
    public ResolveOption()
    {
        AllowCache = false;
        ThrowNonExists = false;
    }

    public FileIntendedAction IntendedAction { get; set; } = FileIntendedAction.None;

    public bool Resolve { get; set; } = true;

    public string FileType { get; set; } = "any";

    public string Namespace { get; set; } = typeof(ResolveOption).Namespace ?? "Cryptodd";

    public bool AllowCache { get; set; }

    public bool ThrowNonExists { get; set; }


    #region IEquatable

    public bool Equals(ResolveOption other) => IntendedAction == other.IntendedAction && Resolve == other.Resolve &&
                                               FileType == other.FileType && Namespace == other.Namespace &&
                                               AllowCache == other.AllowCache && ThrowNonExists == other.ThrowNonExists;

    public override bool Equals(object? obj) => obj is ResolveOption other && Equals(other);

    public override int GetHashCode() =>
        HashCode.Combine((int)IntendedAction, Resolve, FileType, Namespace, AllowCache, ThrowNonExists);

    public static bool operator ==(ResolveOption left, ResolveOption right) => left.Equals(right);

    public static bool operator !=(ResolveOption left, ResolveOption right) => !left.Equals(right);

    #endregion
}