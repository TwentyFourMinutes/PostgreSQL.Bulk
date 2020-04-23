using System;
using System.Collections.Generic;

namespace PostgreSQL.Bulk
{

    internal static class EntityBuilderHelpers
    {
        internal static IEnumerable<TTarget>? FlattenForeignColumns<TEntity, TTarget>(IEnumerable<TEntity> entities, Func<TEntity, IEnumerable<TTarget>?> foreignColumns, Action<TEntity, TTarget> valueCopier) where TEntity : class
                                                                                                                                                                                                            where TTarget : class
        {
            foreach (var entity in entities)
            {
                var foreignColumnsResult = foreignColumns.Invoke(entity);

                if (foreignColumnsResult is null)
                    continue;

                foreach (var foreignColumn in foreignColumnsResult)
                {
                    valueCopier.Invoke(entity, foreignColumn);

                    yield return foreignColumn;
                }
            }
        }

        public static IEnumerable<TTarget>? FlattenForeignColumns<TEntity, TTarget>(IEnumerable<TEntity> entities, Func<TEntity, TTarget?> foreignColumns, Action<TEntity, TTarget> valueCopier) where TEntity : class
                                                                                                                                                                                                 where TTarget : class
        {
            foreach (var entity in entities)
            {
                var foreignColumn = foreignColumns.Invoke(entity);

                if (foreignColumn is null)
                    continue;

                valueCopier.Invoke(entity, foreignColumn);

                yield return foreignColumn;
            }
        }
    }
}
