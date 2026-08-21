namespace LootSingles.Application.Import;

public sealed class OrderPersistenceException(
    string message,
    bool isDuplicate,
    Exception innerException) : Exception(message, innerException)
{
    public bool IsDuplicate { get; } = isDuplicate;
}
