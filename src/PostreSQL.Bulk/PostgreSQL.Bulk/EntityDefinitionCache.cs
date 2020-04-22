using System;
using System.Collections.Concurrent;

namespace PostgreSQL.Bulk
{
    internal static class EntityDefinitionCache
    {
        private static ConcurrentDictionary<Type, IEntityDefinition> _cachedEntities;

        static EntityDefinitionCache()
        {
            _cachedEntities = new ConcurrentDictionary<Type, IEntityDefinition>();
        }

        internal static bool TryAddEntity<TEntityType>(EntityDefinition<TEntityType> entityDefinition, Type? type = null) where TEntityType : class
        {
            return _cachedEntities.TryAdd(type ?? typeof(TEntityType), entityDefinition);
        }

        internal static bool TryGetEntity<TEntityType>(Type? type, out EntityDefinition<TEntityType>? entityDefinition) where TEntityType : class
        {
            var success = _cachedEntities.TryGetValue(type ?? typeof(TEntityType), out var tempEntity);

            entityDefinition = tempEntity as EntityDefinition<TEntityType>;

            return success;
        }

        internal static EntityDefinition<TEntityType> GetOrAddEntity<TEntityType>(Type? type, Func<Type, EntityDefinition<TEntityType>> entityFactory) where TEntityType : class
        {
            return (EntityDefinition<TEntityType>)_cachedEntities.GetOrAdd(type ?? typeof(TEntityType), entityFactory);
        }
    }
}
