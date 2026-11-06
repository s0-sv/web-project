using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace TutorHub.Data;

public static class DbUpdateExceptionExtensions
{
    public static bool IsUniqueViolation(this DbUpdateException e) =>
        e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    public static bool ViolatesIndexOn(this DbUpdateException e, string column) =>
        e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: { } name }
        && name.EndsWith($"_{column}", StringComparison.Ordinal);

    public static bool IsForeignKeyViolation(this DbUpdateException e) =>
        e.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.ForeignKeyViolation or PostgresErrorCodes.RestrictViolation
        };
}
