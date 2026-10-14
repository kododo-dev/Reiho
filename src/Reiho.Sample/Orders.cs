using System.Collections.Concurrent;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Text;
using Kododo.Reiho.AspNetCore.API;

namespace Kododo.Reiho.Sample;

public enum OrderStatus { Open, Cancelled }

public sealed record OrderLine([Range(1, 100)] int Quantity, [StringLength(40)] string Product);

public sealed record Order(int Id, string Customer, IReadOnlyList<OrderLine> Lines, OrderStatus Status);

public sealed class OrderStore
{
    private readonly ConcurrentDictionary<int, Order> _orders = new();
    private int _lastId;

    public Order Add(string customer, IReadOnlyList<OrderLine> lines)
    {
        var order = new Order(Interlocked.Increment(ref _lastId), customer, lines, OrderStatus.Open);
        _orders[order.Id] = order;
        return order;
    }

    public Order? Find(int id) => _orders.GetValueOrDefault(id);

    public IEnumerable<Order> All => _orders.Values.OrderBy(o => o.Id);

    public void Update(Order order) => _orders[order.Id] = order;
}

// ── Requests: data annotations, nested validation, MCP tools ──────────────────

[McpTool, Description("Places an order for a customer.")]
public sealed record PlaceOrder(
    [property: Description("Customer name")] [StringLength(60)] string Customer,
    [property: Description("What to order, one line per product")] [MinLength(1)] IReadOnlyList<OrderLine> Lines) : IRequest<Order>;

[McpTool(ReadOnly = true), Description("Finds the orders of a customer.")]
public sealed record FindOrders([property: Description("Customer name")] string Customer) : IRequest<Order[]>;

[Description("Gets one order.")]
public sealed record GetOrder(int Id) : IRequest<Order>;

[McpTool(Idempotent = true), Description("Cancels an open order.")]
public sealed record CancelOrder(int Id) : IRequest;

public sealed class OrderHandlers(OrderStore store) :
    IRequestHandler<PlaceOrder, Order>,
    IRequestHandler<FindOrders, Order[]>,
    IRequestHandler<GetOrder, Order>,
    IRequestHandler<CancelOrder>
{
    public Task<Order> HandleAsync(PlaceOrder request, CancellationToken cancellationToken) =>
        Task.FromResult(store.Add(request.Customer, request.Lines));

    public Task<Order[]> HandleAsync(FindOrders request, CancellationToken cancellationToken) =>
        Task.FromResult(store.All.Where(o => string.Equals(o.Customer, request.Customer, StringComparison.OrdinalIgnoreCase)).ToArray());

    public Task<Order> HandleAsync(GetOrder request, CancellationToken cancellationToken) =>
        Task.FromResult(store.Find(request.Id) ?? throw RequestException.NotFound($"Order {request.Id} does not exist."));

    public Task HandleAsync(CancelOrder request, CancellationToken cancellationToken)
    {
        var order = store.Find(request.Id) ?? throw RequestException.NotFound($"Order {request.Id} does not exist.");
        if (order.Status == OrderStatus.Cancelled)
            throw RequestException.Conflict($"Order {request.Id} is already cancelled.");
        store.Update(order with { Status = OrderStatus.Cancelled });
        return Task.CompletedTask;
    }
}

// ── Files: a CSV download, and an upload that needs an antiforgery token ──────

[McpTool(ReadOnly = true), Description("Exports all orders as CSV.")]
public sealed record ExportOrders : IRequest<FileResponse>;

[Description("Imports orders from a CSV file with customer,product,quantity rows.")]
public sealed record ImportOrders : IRequest<int>
{
    [Required]
    public IFormFile? File { get; init; }
}

public sealed class FileHandlers(OrderStore store) :
    IRequestHandler<ExportOrders, FileResponse>,
    IRequestHandler<ImportOrders, int>
{
    public Task<FileResponse> HandleAsync(ExportOrders request, CancellationToken cancellationToken)
    {
        var csv = new StringBuilder("id,customer,product,quantity,status\n");
        foreach (var order in store.All)
            foreach (var line in order.Lines)
                csv.Append($"{order.Id},{order.Customer},{line.Product},{line.Quantity},{order.Status}\n");
        return Task.FromResult(FileResponse.FromText(csv.ToString(), "text/csv", "orders.csv"));
    }

    public async Task<int> HandleAsync(ImportOrders request, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(request.File!.OpenReadStream());
        var imported = 0;
        var row = 0;
        while (await reader.ReadLineAsync(cancellationToken) is { } text)
        {
            row++;
            if (text.Split(',') is not [var customer, var product, var quantity] || !int.TryParse(quantity, out var count))
                throw RequestException.Invalid(nameof(request.File), $"Row {row} is not customer,product,quantity.");
            store.Add(customer, [new OrderLine(count, product)]);
            imported++;
        }
        return imported;
    }
}

// ── A request filter: runs for HTTP and MCP alike, and sees the request ──────

public sealed class TimingFilter(ILogger<TimingFilter> logger) : IRequestFilter
{
    public async ValueTask<object?> InvokeAsync(RequestFilterContext context, RequestFilterDelegate next)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            return await next(context);
        }
        finally
        {
            logger.LogInformation("{Request} over {Transport} took {Elapsed} ms",
                context.Request.GetType().Name, context.Transport, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }
    }
}
