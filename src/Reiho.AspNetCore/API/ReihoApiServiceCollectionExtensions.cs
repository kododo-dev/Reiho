using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Kododo.Reiho.AspNetCore.API;

public static class ReihoApiServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddRequestHandlers()
        {
            return services.AddRequestHandlers(Assembly.GetCallingAssembly());
        }

        public IServiceCollection AddRequestHandlers(Assembly assembly)
        {
            assembly.GetTypes()
                .Where(type => type is { IsAbstract: false, IsInterface: false })
                .SelectMany(type => type.GetInterfaces(), (type, interfaceType) => new { type, interfaceType })
                .Where(t => t.interfaceType.IsGenericType &&
                            (t.interfaceType.GetGenericTypeDefinition() == typeof(IRequestHandler<>) ||
                             t.interfaceType.GetGenericTypeDefinition() == typeof(IRequestHandler<,>)))
                .ToList()
                .ForEach(t => services.AddTransient(t.interfaceType, t.type));

            return services;
        }
    }
}