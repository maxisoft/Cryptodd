using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text;
using Cryptodd.Ftx.Models;
using Cryptodd.IoC;
using Lamar;

namespace Cryptodd.Ftx.Orderbooks;

public class ValidatorDetails<T>
{
    public Dictionary<string, string> InvalidFields { get; internal set; } = new Dictionary<string, string>();
}

[Singleton]
public sealed class AsciiStringValidator : IValidator<string>, IService
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Validate(in string value, out ValidatorDetails<string>? details)
    {
        details = null;
        return Encoding.UTF8.GetByteCount(value) == value.Length;
    }
}

public class FtxOrderbookValidator : IValidator<GroupedOrderbookDetails>, IService
{
    private readonly AsciiStringValidator _stringValidator;
    private readonly DateTimeOffset MinimalDate = new DateTimeOffset(2008, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public FtxOrderbookValidator(AsciiStringValidator stringValidator)
    {
        _stringValidator = stringValidator;
    }

    public bool Validate(in GroupedOrderbookDetails value, out ValidatorDetails<GroupedOrderbookDetails>? details)
    {
        var res = true;
        details = new ValidatorDetails<GroupedOrderbookDetails>();
        if (value.Grouping <= 0 || !double.IsFinite(value.Grouping))
        {
            details.InvalidFields["Grouping"] = "Negative or not normal";
            res = false;
        }

        if (string.IsNullOrWhiteSpace(value.Market))
        {
            details.InvalidFields["Market"] = "Empty";
            res = false;
        }
        else if (!_stringValidator.Validate(value.Market, out _))
        {
            details.InvalidFields["Market"] = "Non-ascii";
            res = false;
        }

        if (value.Time <= MinimalDate)
        {
            details.InvalidFields["Time"] = "Too old";
            res = false;
        }
        
        if (value.Time > DateTimeOffset.UtcNow + TimeSpan.FromDays(1))
        {
            details.InvalidFields["Time"] = "In the future";
            res = false;
        }
        
        if (!value.Data.Asks.Any() && !value.Data.Bids.Any())
        {
            details.InvalidFields["Data"] = "Empty";
        }
        else
        {
            res = res & ValidateAsks(value, details) & ValidateBids(value, details);
        }
        return res;
    }

    private static bool ValidateAsks(GroupedOrderbookDetails value, ValidatorDetails<GroupedOrderbookDetails> details)
    {
        var res = true;
        foreach (var pair in value.Data.Asks)
        {
            if (pair.Price <= 0 || !double.IsNormal(pair.Price))
            {
                details.InvalidFields["Data.Asks.Price"] = "Negative or not normal";
                res = false;
            }

            if (pair.Size < 0 || !double.IsFinite(pair.Size))
            {
                details.InvalidFields["Data.Asks.Size"] = "Negative or not normal";
                res = false;
            }
        }

        return res;
    }
    
    private static bool ValidateBids(GroupedOrderbookDetails value, ValidatorDetails<GroupedOrderbookDetails> details)
    {
        var res = true;
        foreach (var pair in value.Data.Bids)
        {
            if (pair.Price < 0 || !double.IsFinite(pair.Price))
            {
                details.InvalidFields["Data.Bids.Price"] = "Negative or not finite";
                res = false;
            }
            
            if (pair.Size < 0 || !double.IsFinite(pair.Size))
            {
                details.InvalidFields["Data.Bids.Size"] = "Negative or not normal";
                res = false;
            }
        }

        return res;
    }


    public bool Validate(in string s, out ValidatorDetails<string>? validatorDetails) => throw new NotImplementedException();
}

public interface IValidator<T>
{
    bool Validate(in T s, out ValidatorDetails<T>? validatorDetails);
}