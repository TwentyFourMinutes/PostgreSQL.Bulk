using Npgsql;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace PostgreSQL.Bulk
{
    internal class ColumnDefinition<TEntity> where TEntity : class
    {
        internal string ColumnName { get; }

        private readonly Func<TEntity, NpgsqlBinaryImporter, CancellationToken, Task> _valueWriter;

        internal ColumnDefinition(string columnName, Func<TEntity, NpgsqlBinaryImporter, CancellationToken, Task> valueWriter)
        {
            ColumnName = columnName;
            _valueWriter = valueWriter;
        }

        internal Task WriteValues(TEntity entity, NpgsqlBinaryImporter binaryImporter, CancellationToken cancellationToken)
        {
            return _valueWriter.Invoke(entity, binaryImporter, cancellationToken);
        }
    }
}
