using Lamar;
using Lamar.IoC.Instances;
using Maxisoft.Utils.Objects;
using Microsoft.Extensions.DependencyInjection;

namespace CryptoDumper.IoC.Registries
{
    public interface ICancellationTokenRegistry
    {
        CancellationTokenSource CancellationTokenSource { get; }
        CancellationTokenRegistry.CancellationTokenPolicy Policy { get; }
    }

    public class CancellationTokenRegistry : ServiceRegistry, ICancellationTokenRegistry
    {
        public CancellationTokenSource CancellationTokenSource { get; }
        public CancellationTokenPolicy Policy { get; }

        public CancellationTokenRegistry()
        {
            CancellationTokenSource = new CancellationTokenSource();
            ForSingletonOf<CancellationTokenSource>().Use(CancellationTokenSource);
            For<Boxed<CancellationToken>>().Use(ctx => ctx.GetInstance<CancellationTokenSource>().Token);
            Policy = new CancellationTokenPolicy(CancellationTokenSource);
            Policies.Add(Policy);
            ForSingletonOf<ICancellationTokenRegistry>().Use(this);
        }


        public class CancellationTokenPolicy : ConfiguredInstancePolicy
        {
            public CancellationTokenSource CancellationTokenSource { get; set; }

            public CancellationTokenPolicy(CancellationTokenSource cancellationTokenSource)
            {
                CancellationTokenSource = cancellationTokenSource;
            }

            protected override void apply(IConfiguredInstance instance)
            {
                if (instance is ConstructorInstance ci)
                {
                    if (ci.Arguments.Any(arg =>
                        typeof(CancellationToken).IsAssignableFrom(arg.Parameter.ParameterType)))
                    {
                        instance.AddInline(new LambdaInstance(typeof(CancellationToken),
                            _ => CancellationTokenSource.Token,
                            ServiceLifetime.Transient));
                    }
                }
                else if (instance.Constructor
                             ?.GetParameters().Any(param =>
                                 typeof(CancellationToken).IsAssignableFrom(param.ParameterType)) ??
                         false)
                {
                    instance.AddInline(new LambdaInstance(typeof(CancellationToken),
                        _ => CancellationTokenSource.Token,
                        ServiceLifetime.Transient));
                }
            }
        }
    }
}