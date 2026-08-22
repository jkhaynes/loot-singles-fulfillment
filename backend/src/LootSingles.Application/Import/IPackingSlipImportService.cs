namespace LootSingles.Application.Import;

public interface IPackingSlipImportService
{
    IAsyncEnumerable<ImportProgressUpdate> ImportAsync(
        Stream packingSlipPdf,
        CancellationToken cancellationToken = default
    );
}
