# Reiho sample

A small order service that uses every part of Reiho: requests and handlers, validation and problem details, `RequestException`, files with antiforgery, a request filter, OpenAPI, MCP tools, logs and traces.

```bash
dotnet run --project src/Reiho.Sample --urls http://localhost:5080
```

| | |
|---|---|
| `Orders.cs` | the requests, their handlers, the CSV export and import, and `TimingFilter` |
| `Program.cs` | the setup: `MapRequests` under `/api`, OpenAPI, MCP at `/mcp`, antiforgery |

## Try it

```bash
B=http://localhost:5080

# An order, then one that is not valid: lines[0].quantity must be 1-100
curl -H Content-Type:application/json -d '{"customer":"Ada","lines":[{"quantity":2,"product":"tea"}]}' $B/api/PlaceOrder
curl -H Content-Type:application/json -d '{"customer":"Bo","lines":[{"quantity":0,"product":"tea"}]}' $B/api/PlaceOrder

# 404 and 409 from RequestException
curl -H Content-Type:application/json -d '{"id":99}' $B/api/GetOrder
curl -H Content-Type:application/json -d '{"id":1}' $B/api/CancelOrder   # 204, then 409 the second time

# A CSV download
curl -X POST -OJ $B/api/ExportOrders

# An upload: a form post needs the antiforgery token and its cookie
TOKEN=$(curl -s -c cookies $B/antiforgery | tr -d '"')
printf 'Cy,coffee,3\n' > orders.csv
curl -b cookies -F file=@orders.csv -F __RequestVerificationToken=$TOKEN $B/api/ImportOrders
```

- **OpenAPI** document: `$B/openapi/v1.json`.
- **MCP** endpoint: `$B/mcp`. It offers `place_order`, `find_orders`, `cancel_order` and `export_orders`. Point an MCP client such as the MCP Inspector at it.
- **Logs:** the console shows each request's time from `TimingFilter`, and debug logs for requests turned away.

The MCP endpoint is open here to keep the sample short. A real app protects it with `RequireAuthorization()`.
