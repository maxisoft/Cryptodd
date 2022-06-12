using Cryptodd.Cli;
using Typin;
using Typin.Attributes;
using Typin.Console;

[Command]
public class MainCommand : BaseCommand, ICommand
{
    public override async ValueTask ExecuteAsync(IConsole console)
    {
        console.GetCancellationToken().Register(() => Container.GetInstance<CancellationTokenSource>().Cancel());

        await PreExecute();
        try
        {
            await SchedulerLoop();
        }
        catch (Exception e) when (e is TaskCanceledException or TimeoutException or OperationCanceledException)
        {
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