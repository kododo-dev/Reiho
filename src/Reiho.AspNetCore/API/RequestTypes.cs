using System.Reflection;

namespace Kododo.Reiho.AspNetCore.API;

/// <summary>A request type found in an assembly, with the type of its result, or <c>null</c> for an <see cref="IRequest"/>.</summary>
internal sealed record RequestType(Type Request, Type? Result)
{
    /// <summary>The concrete, non-generic request types of <paramref name="assembly"/>.</summary>
    public static IEnumerable<RequestType> Find(Assembly assembly)
    {
        foreach (var type in assembly.GetTypes())
        {
            if (type is not { IsClass: true, IsAbstract: false } || type.IsGenericType)
                continue;
            if (typeof(IRequest).IsAssignableFrom(type))
                yield return new RequestType(type, null);
            else if (type.GetInterfaces().FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IRequest<>)) is { } withResult)
                yield return new RequestType(type, withResult.GetGenericArguments()[0]);
        }
    }
}
