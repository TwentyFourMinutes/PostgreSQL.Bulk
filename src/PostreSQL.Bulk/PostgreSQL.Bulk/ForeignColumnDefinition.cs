using Npgsql;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PostgreSQL.Bulk
{
    internal class ForeignColumnDefinition<TEntity> where TEntity : class
    {
        internal Func<IEnumerable<TEntity>, NpgsqlConnection, CancellationToken, ValueTask<ulong>> WriteValues { get; }

        internal ForeignColumnDefinition(Func<IEnumerable<TEntity>, NpgsqlConnection, CancellationToken, ValueTask<ulong>> valueWriter)
        {
            WriteValues = valueWriter;
        }
    }
}
