using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Paper.Web.Features.Documents;

internal static class DocumentPersistenceErrors
{
    public static bool IsDuplicateHash(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "IX_Documents_Hash"
        };
}
