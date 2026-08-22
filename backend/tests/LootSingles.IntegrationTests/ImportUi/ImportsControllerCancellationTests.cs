using LootSingles.Application.Import;

namespace LootSingles.IntegrationTests.ImportUi;

public sealed class ImportsControllerCancellationTests
{
    [Fact]
    public void ImportServiceContractAcceptsCancellationToken()
    {
        var method = typeof(IPackingSlipImportService).GetMethod(
            nameof(IPackingSlipImportService.ImportAsync)
        );

        Assert.Equal(typeof(CancellationToken), method!.GetParameters()[1].ParameterType);
    }
}
