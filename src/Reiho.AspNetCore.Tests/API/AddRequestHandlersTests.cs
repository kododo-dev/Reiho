using System.Reflection;
using Kododo.Reiho.AspNetCore.API;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Kododo.Reiho.AspNetCore.Tests.API;

public sealed class AddRequestHandlersTests
{
    private static IServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddRequestHandlers(Assembly.GetExecutingAssembly());
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Registers_void_handler()
    {
        var provider = BuildProvider();
        var handler = provider.GetService<IRequestHandler<PingRequest>>();
        Assert.NotNull(handler);
        Assert.IsType<PingHandler>(handler);
    }

    [Fact]
    public void Registers_void_handler_with_constructor_parameter()
    {
        var provider = BuildProvider();
        var handler = provider.GetService<IRequestHandler<DeleteItemRequest>>();
        Assert.NotNull(handler);
        Assert.IsType<DeleteItemHandler>(handler);
    }

    [Fact]
    public void Registers_result_handler()
    {
        var provider = BuildProvider();
        var handler = provider.GetService<IRequestHandler<GetGreetingRequest, GreetingResult>>();
        Assert.NotNull(handler);
        Assert.IsType<GetGreetingHandler>(handler);
    }

    [Fact]
    public void Handlers_are_registered_as_transient()
    {
        var provider = BuildProvider();
        var a = provider.GetRequiredService<IRequestHandler<PingRequest>>();
        var b = provider.GetRequiredService<IRequestHandler<PingRequest>>();
        Assert.NotSame(a, b);
    }

    [Fact]
    public void Does_not_throw_for_assembly_with_no_handlers()
    {
        // Scanning an assembly that has no IRequestHandler implementations should be a no-op.
        var services = new ServiceCollection();
        var exception = Record.Exception(() =>
            services.AddRequestHandlers(typeof(object).Assembly)); // mscorlib — zero handlers
        Assert.Null(exception);
    }

    [Fact]
    public void Does_not_register_handler_for_request_with_no_handler()
    {
        var provider = BuildProvider();
        // UnhandledRequest implements IRequest but has no handler class in this assembly
        var handler = provider.GetService<IRequestHandler<UnhandledRequest>>();
        Assert.Null(handler);
    }
}
