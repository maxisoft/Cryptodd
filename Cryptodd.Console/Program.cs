using System.Text;
using Cryptodd.Scheduler.Tasks;
using Typin;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
await new CliApplicationBuilder()
    .AddCommandsFromThisAssembly()
    .AddCommandsFrom(typeof(BaseScheduledTask).Assembly)
    .Build()
    .RunAsync();