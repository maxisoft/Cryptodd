using Cryptodd.Bitfinex;
using Cryptodd.Bitfinex.WebSockets;
using Cryptodd.Cli;
using Maxisoft.Utils.Objects;
using Npgsql;
using Typin;
using Typin.Attributes;
using Typin.Console;

namespace Cryptodd.Console.Cli;

[Command]
public class MainCommand : BaseCommand, ICommand
{
    private const int StatementCancelledErrorCode = 57014;

    public override async ValueTask ExecuteAsync(IConsole console)
    {
        await PreExecute(console);
        try
        {
            await SchedulerLoop();
        }
        catch (Exception e) when (e is TaskCanceledException or TimeoutException or OperationCanceledException
                                      or PostgresException)
        {
            if (e is PostgresException pgException && pgException.ErrorCode != StatementCancelledErrorCode)
            {
                throw;
            }

            if (!Container.GetInstance<CancellationTokenSource>().IsCancellationRequested)
            {
                Logger.Error(e, "Application is now crashing");
                throw;
            }

            if (console.GetCancellationToken().IsCancellationRequested)
            {
                Logger.Debug("User requested cancellation via ctrl+c");
            }
        }
    }
}