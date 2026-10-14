using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Kododo.Reiho.AspNetCore.API;

public static class ReihoApiServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddRequestHandlers(Assembly assembly)
        {
            assembly.GetTypes()
                .Where(type => type is { IsAbstract: false, IsInterface: false })
                .SelectMany(type => type.GetInterfaces(), (type, interfaceType) => new { type, interfaceType })
                .Where(t => t.interfaceType.IsGenericType &&
                            (t.interfaceType.GetGenericTypeDefinition() == typeof(IRequestHandler<>) ||
                             t.interfaceType.GetGenericTypeDefinition() == typeof(IRequestHandler<,>)))
                .ToList()
                // TryAdd: calling it again for the same assembly, e.g. from two libraries, adds nothing, and a handler
                // the app registered first takes the place of the assembly's own.
                .ForEach(t => services.TryAddTransient(t.interfaceType, t.type));

            return services;
        }
    }
}