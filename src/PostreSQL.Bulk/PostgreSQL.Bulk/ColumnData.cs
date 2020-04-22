using Npgsql;
using NpgsqlTypes;
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace PostgreSQL.Bulk
{

    internal class ColumnData<TEntity> where TEntity : class
    {
        internal string ColumnName { get; set; }

        internal NpgsqlDbType? NpgsqlDbType { get; set; }

        internal LambdaExpression? ValueFactory { get; set; }
        internal LambdaExpression? ValueValidator { get; set; }

        internal Func<IEnumerable<TEntity>, NpgsqlConnection, CancellationToken, Task<ulong>>? ForeignColumnsWriter { get; set; }

        internal PropertyInfo ColumnInfo { get; }

        internal ColumnData(PropertyInfo propertyInfo)
        {
            ColumnInfo = propertyInfo;
            ColumnName = propertyInfo.Name;
        }
    }
}
