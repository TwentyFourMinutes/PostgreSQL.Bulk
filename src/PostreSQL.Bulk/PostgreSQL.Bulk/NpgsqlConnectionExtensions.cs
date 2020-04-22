using Npgsql;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PostgreSQL.Bulk
{
    public static class NpgsqlConnectionExtensions
    {
        public static Task<ulong> BulkInsertAsync<TEntity>(this NpgsqlConnection connection, IEnumerable<TEntity> entities, CancellationToken cancellationToken = default) where TEntity : class
        {
            if (!EntityConfigurator.IsBuild)
            {
                EntityConfigurator.BuildConfigurations();
            }

            EntityDefinitionCache.TryGetEntity<TEntity>(null, out var entityDefinition);

            var copyStatement = CompileCopyStatement(entityDefinition);

            return PerformCopy(connection, entityDefinition, entities, copyStatement, cancellationToken);
        }
        private static async Task<ulong> PerformCopy<TEntity>(NpgsqlConnection connection, EntityDefinition<TEntity> entityDefinition, IEnumerable<TEntity> entities, string command, CancellationToken cancellationToken) where TEntity : class
        {
            var binaryImporter = connection.BeginBinaryImport(command);

            foreach (var entity in entities)
            {
                await binaryImporter.StartRowAsync();

                for (int entityColumnIndex = 0; entityColumnIndex < entityDefinition.ColumnDefinitions.Count; entityColumnIndex++)
                {
                    var entityColumn = entityDefinition.ColumnDefinitions[entityColumnIndex];

                    await entityColumn.WriteValues(entity, binaryImporter, cancellationToken);
                }
            }

            return await CompleteAndWriteRelationValues(connection, binaryImporter, entityDefinition, entities, cancellationToken);
        }

        private static async Task<ulong> CompleteAndWriteRelationValues<TEntity>(NpgsqlConnection connection, NpgsqlBinaryImporter binaryImporter, EntityDefinition<TEntity> entityDefinition, IEnumerable<TEntity> entities, CancellationToken cancellationToken) where TEntity : class
        {
            ulong insertCount = await binaryImporter.CompleteAsync(cancellationToken);

            await binaryImporter.DisposeAsync();

            for (int columnIndex = 0; columnIndex < entityDefinition.ForeignColumnDefinitions.Count; columnIndex++)
            {
                var column = entityDefinition.ForeignColumnDefinitions[columnIndex];

                insertCount += await column.WriteValues(connection, entities, cancellationToken);
            }

            return insertCount;
        }

        private static string CompileCopyStatement<TEntity>(EntityDefinition<TEntity> entityDefinition) where TEntity : class
        {
            var sb = new StringBuilder();

            sb.Append("COPY ");
            sb.Append('"');
            sb.Append(entityDefinition.TableName);
            sb.Append('"');
            sb.Append("(");

            for (int i = 0; i < entityDefinition.ColumnDefinitions.Count; i++)
            {
                sb.Append('"');
                sb.Append(entityDefinition.ColumnDefinitions[i].ColumnName);
                sb.Append('"');
                sb.Append(", ");
            }

            sb.Remove(sb.Length - 2, 2);

            sb.Append(") ");
            sb.Append("FROM STDIN BINARY;");

            return sb.ToString();
        }
    }
}
