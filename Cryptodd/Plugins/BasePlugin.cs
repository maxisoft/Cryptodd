using System;
using Lamar;
using LamarCodeGeneration;
using Serilog;

namespace Cryptodd.Plugins
{
    public abstract class BasePlugin : IBasePlugin
    {
        protected BasePlugin(IContainer container)
        {
            Container = container;
            Logger = container.GetInstance<ILogger>().ForContext(GetType());
        }

        public virtual string Name => GetType().Name;
        public virtual Version Version => GetType().Assembly.GetName().Version ?? new Version();
        
        public ILogger Logger { get; protected set; }

        protected IContainer Container { get; }

        public virtual Task OnStart()
        {
            return Task.CompletedTask;
        }

        public int Order { get; protected set; } = 0;

        public virtual ValueTask DisposeAsync()
        {
            GC.SuppressFinalize(this);
            return ValueTask.CompletedTask;
        }
    }
}