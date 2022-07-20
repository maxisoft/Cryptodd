using System.Text;
using Cryptodd.Scheduler.Tasks;
using Typin;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
return await new CliApplicationBuilder()
    .AddCommandsFromThisAssembly()
    .AddCommandsFrom(typeof(BaseScheduledTask).Assembly)
    .Build()
    .RunAsync();