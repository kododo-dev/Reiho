using System.ComponentModel.DataAnnotations;
using Kododo.Reiho.AspNetCore.API;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;

namespace Kododo.Reiho.AspNetCore.Tests.API;

// ── Uploads ───────────────────────────────────────────────────────────────────

[RequireAntiforgeryToken(false)]
public sealed record UploadAvatarRequest(int UserId, Color Tint) : IRequest<string>
{
    [Required]
    public IFormFile? Avatar { get; init; }

    public IReadOnlyList<IFormFile> Extras { get; init; } = [];
}

public sealed class UploadAvatarHandler : IRequestHandler<UploadAvatarRequest, string>
{
    public Task<string> HandleAsync(UploadAvatarRequest request, CancellationToken cancellationToken) =>
        Task.FromResult($"{request.UserId}:{request.Tint}:{request.Avatar!.FileName}:{request.Avatar.Length}:{request.Extras.Count}");
}

// Keeps the default: a form post needs an antiforgery token.
public sealed record AttachNoteRequest(string Note, IFormFile? File) : IRequest<string>;

public sealed class AttachNoteHandler : IRequestHandler<AttachNoteRequest, string>
{
    public Task<string> HandleAsync(AttachNoteRequest request, CancellationToken cancellationToken) =>
        Task.FromResult($"{request.Note}:{request.File?.FileName}");
}

// ── Downloads ─────────────────────────────────────────────────────────────────

public sealed record ExportGreetingsRequest(string Format) : IRequest<FileResponse>;

public sealed class ExportGreetingsHandler : IRequestHandler<ExportGreetingsRequest, FileResponse>
{
    public Task<FileResponse> HandleAsync(ExportGreetingsRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(request.Format == "csv"
            ? FileResponse.FromText("name\nAda", "text/csv", "greetings.csv")
            : FileResponse.FromBytes([1, 2, 3]));
}

// ── A request without a result for tests that only need one; PingRequest's call count belongs to RequestHandlingTests.

public sealed record NoopRequest : IRequest;

public sealed class NoopHandler : IRequestHandler<NoopRequest>
{
    public Task HandleAsync(NoopRequest request, CancellationToken cancellationToken) => Task.CompletedTask;
}
