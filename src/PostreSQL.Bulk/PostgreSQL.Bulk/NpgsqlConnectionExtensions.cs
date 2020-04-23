using Npgsql;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PostgreSQL.Bulk
{
    /// <summary>
    /// Provides extension methods to the <see cref="NpgsqlConnection"/> class, which help with bulk insert.
    /// </summary>
    public static class NpgsqlConnectionExtensions
    {
        /// <summary>
        /// Bulk inserts a collection of entities and its foreign columns, using the PostgreSQL COPY command.
        /// </summary>
        /// <typeparam name="TEntity">The type of the top most collection of entities.</typeparam>
        /// <param name="connection">A open connection to your database, which is not already in a COPY state.</param>
        /// <param name="entities">A collection containing all the entities.</param>
        /// <param name="cancellationToken">A CancellationToken instance which can cancel the current operation.</param>
        /// <returns>The Task object which represents the current operation and the amount of inserted rows.</returns>
        /// <exception cref="TypeArgumentException">Thrown when no matching build configuration for the passed <typeparamref name="TEntity"/> could be found.</exception>
        public static ValueTask<ulong> BulkInsertAsync<TEntity>(this NpgsqlConnection connection, IEnumerable<TEntity> entities, CancellationToken cancellationToken = default) where TEntity : class
        {
            if (entities is null)
            {
                return new ValueTask<ulong>(Task.FromResult((ulong)0));
            }

            if (!EntityConfigurator.IsBuild)
            {
                EntityConfigurator.BuildConfigurations();
            }

            if (!EntityDefinitionCache.TryGetEntity<TEntity>(null, out var entityDefinition))
            {
                throw new TypeArgumentException($"The type {nameof(TEntity)} does not have a valid configuration already build and no was found in the current assembly. Try calling EntityConfigurator.BuildConfiguration explicitly at the start of your application.");
            }

            var copyStatement = CompileCopyStatement(entityDefinition!);

            return new ValueTask<ulong>(PerformCopy(connection, entityDefinition!, entities, copyStatement, cancellationToken));
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

                insertCount += await column.WriteValues(entities, connection, cancellationToken);
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
