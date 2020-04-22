using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace PostgreSQL.Bulk
{
    internal class EntityDefinition<TEntity> : IEntityDefinition where TEntity : class
    {
        internal string TableName { get; }

        internal ReadOnlyCollection<ColumnDefinition<TEntity>> ColumnDefinitions { get; }

        internal ReadOnlyCollection<ForeignColumnDefinition<TEntity>> ForeignColumnDefinitions { get; }

        internal EntityDefinition(string tableName, List<ColumnDefinition<TEntity>> columnDefinitions, List<ForeignColumnDefinition<TEntity>> foreignColumnDefinitions)
        {
            TableName = tableName;
            ColumnDefinitions = new ReadOnlyCollection<ColumnDefinition<TEntity>>(columnDefinitions);
            ForeignColumnDefinitions = new ReadOnlyCollection<ForeignColumnDefinition<TEntity>>(foreignColumnDefinitions);
        }
    }
}
