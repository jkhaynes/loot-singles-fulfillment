using Microsoft.Data.SqlClient;

namespace LootSingles.Infrastructure.Persistence;

/// <summary>
/// Recognizes a unique-constraint/index violation from a <see cref="Microsoft.EntityFrameworkCore.DbUpdateException"/>
/// from SQL Server, so persistence code can translate it into a typed application-level result
/// instead of an opaque database exception.
/// </summary>
internal static class DuplicateKeyDetector
{
    public static bool IsDuplicateKeyViolation(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (
                current is SqlException sqlException
                && sqlException.Errors.Cast<SqlError>().Any(error => error.Number is 2601 or 2627)
            )
            {
                return true;
            }
        }

        return false;
    }
}
