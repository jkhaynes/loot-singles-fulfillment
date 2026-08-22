using System.Text.Json;
using LootSingles.Application.Import;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace LootSingles.Api.Controllers;

[ApiController]
[Route("api/imports")]
[Authorize]
public sealed class ImportsController(
    IPackingSlipImportService importService,
    IOptions<JsonOptions> jsonOptions
) : ControllerBase
{
    public const long MaximumFileBytes = 26_214_400;

    [HttpPost]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaximumFileBytes + 1_048_576)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaximumFileBytes + 1_048_576)]
    public async Task Post()
    {
        if (!Request.HasFormContentType)
        {
            await WriteProblemAsync(
                StatusCodes.Status400BadRequest,
                "A multipart form containing one PDF file is required."
            );
            return;
        }

        IFormCollection form;

        try
        {
            form = await Request.ReadFormAsync(HttpContext.RequestAborted);
        }
        catch (Exception exception)
            when (exception is InvalidDataException or BadHttpRequestException)
        {
            await WriteProblemAsync(
                StatusCodes.Status400BadRequest,
                "The multipart form could not be read."
            );
            return;
        }

        var files = form.Files.Where(candidate => candidate.Name == "file").ToArray();

        if (files.Length != 1 || form.Files.Count != 1)
        {
            await WriteProblemAsync(
                StatusCodes.Status400BadRequest,
                "Exactly one file field named 'file' is required."
            );
            return;
        }

        var file = files[0];

        if (!string.Equals(file.ContentType, "application/pdf", StringComparison.OrdinalIgnoreCase))
        {
            await WriteProblemAsync(
                StatusCodes.Status415UnsupportedMediaType,
                "The selected file must be a PDF."
            );
            return;
        }

        if (file.Length > MaximumFileBytes)
        {
            await WriteProblemAsync(
                StatusCodes.Status413PayloadTooLarge,
                "The selected PDF exceeds the 25 MB limit."
            );
            return;
        }

        Response.StatusCode = StatusCodes.Status200OK;
        Response.ContentType = "application/x-ndjson";

        await using var stream = file.OpenReadStream();
        await StreamSnapshotsAsync(stream, HttpContext.RequestAborted);
    }

    private async Task StreamSnapshotsAsync(Stream stream, CancellationToken cancellationToken)
    {
        ImportSnapshot? lastSnapshot = null;
        var lastProcessedCount = 0;
        var terminalSnapshotWritten = false;

        try
        {
            await foreach (
                var update in importService
                    .ImportAsync(stream, cancellationToken)
                    .WithCancellation(cancellationToken)
            )
            {
                var status = update.IsComplete
                    ? ImportOperationStatus.Completed
                    : ImportOperationStatus.InProgress;

                lastSnapshot = ImportSnapshot.From(update, status);

                if (!update.IsComplete && update.OrdersProcessed <= lastProcessedCount)
                {
                    continue;
                }

                if (update.IsComplete)
                {
                    terminalSnapshotWritten = true;
                }
                else
                {
                    lastProcessedCount = update.OrdersProcessed;
                }

                await WriteSnapshotAsync(lastSnapshot, cancellationToken);
            }

            if (!terminalSnapshotWritten)
            {
                var completedSnapshot = lastSnapshot is null
                    ? ImportSnapshot.Empty(ImportOperationStatus.Completed)
                    : lastSnapshot with
                    {
                        Status = ImportOperationStatus.Completed,
                    };

                await WriteSnapshotAsync(completedSnapshot, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            var failedSnapshot = (
                lastSnapshot ?? ImportSnapshot.Empty(ImportOperationStatus.Failed)
            ) with
            {
                Status = ImportOperationStatus.Failed,
                OperationFailureMessage =
                    "The import could not be completed. "
                    + "Completed orders remain imported and it is safe to retry.",
            };

            await WriteSnapshotAsync(failedSnapshot, cancellationToken);
        }
    }

    private async Task WriteSnapshotAsync(
        ImportSnapshot snapshot,
        CancellationToken cancellationToken
    )
    {
        await JsonSerializer.SerializeAsync(
            Response.Body,
            snapshot,
            jsonOptions.Value.JsonSerializerOptions,
            cancellationToken
        );

        await Response.WriteAsync("\n", cancellationToken);
        await Response.Body.FlushAsync(cancellationToken);
    }

    private async Task WriteProblemAsync(int status, string detail)
    {
        Response.StatusCode = status;

        var problem = new ProblemDetails
        {
            Status = status,
            Title = ReasonPhrases.GetReasonPhrase(status),
            Detail = detail,
        };

        await Response.WriteAsJsonAsync(problem, jsonOptions.Value.JsonSerializerOptions);
    }

    private enum ImportOperationStatus
    {
        InProgress,
        Completed,
        Failed,
    }

    private sealed record ImportResultSnapshot(
        string? SourceOrderIdentifier,
        ImportOutcome Outcome,
        FailureType? FailureCode,
        string? FailureMessage,
        int? ResultingOrderId
    );

    private sealed record ImportSnapshot(
        ImportOperationStatus Status,
        int OrdersDetected,
        int OrdersProcessed,
        int SucceededCount,
        int FailedCount,
        FailureType? AttemptFailureCode,
        string? AttemptFailureMessage,
        string? OperationFailureMessage,
        IReadOnlyList<ImportResultSnapshot> Results
    )
    {
        public static ImportSnapshot Empty(ImportOperationStatus status) =>
            new(
                status,
                OrdersDetected: 0,
                OrdersProcessed: 0,
                SucceededCount: 0,
                FailedCount: 0,
                AttemptFailureCode: null,
                AttemptFailureMessage: null,
                OperationFailureMessage: null,
                Results: []
            );

        public static ImportSnapshot From(
            ImportProgressUpdate update,
            ImportOperationStatus status
        ) =>
            new(
                status,
                update.OrdersDetected,
                update.OrdersProcessed,
                update.SucceededCount,
                update.FailedCount,
                update.ImportAttempt.AttemptFailureCode,
                update.ImportAttempt.AttemptFailureMessage,
                OperationFailureMessage: null,
                update
                    .ImportAttempt.ImportOrderResults.Select(result => new ImportResultSnapshot(
                        result.SourceOrderIdentifier,
                        result.Outcome,
                        result.FailureCode,
                        result.FailureMessage,
                        result.ResultingOrderId
                    ))
                    .ToArray()
            );
    }
}
