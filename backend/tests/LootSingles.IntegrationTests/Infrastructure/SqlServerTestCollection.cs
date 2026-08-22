namespace LootSingles.IntegrationTests.Infrastructure;

[CollectionDefinition(Name, DisableParallelization = false)]
public sealed class SqlServerTestCollection : ICollectionFixture<SqlServerContainerFixture>
{
    public const string Name = "SQL Server integration";
}
