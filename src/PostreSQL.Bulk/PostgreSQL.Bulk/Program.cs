using Npgsql;
using NpgsqlTypes;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace PostgreSQL.Bulk
{
    class Program
    {
        static void Main(string[] args)
        {


        }
    }

    public class Person
    {
        public Guid Id { get; set; }

        public string Name { get; set; }

        public List<Email> Emails { get; set; }
    }

    public class Email
    {
        public Guid Id { get; set; }

        public Guid PersonId { get; set; }

        public string Address { get; set; }
    }

    public class EntityBuilder<TEntity> where TEntity : class
    {
        internal Dictionary<string, ColumnData<TEntity>> ColumnData { get; }
        internal List<string> IgnoredColumns { get; }

        private static readonly Type _binaryImporterType;
        private static readonly Type _cancellationTokenType;
        private static readonly Type _npgsqlDbType;

        private readonly Type _entityType;

        private static readonly ParameterExpression _binaryImporterParameter;
        private static readonly ParameterExpression _cancellationTokenParameter;

        private readonly ParameterExpression _entityParameter;

        private string _tableName;

        static EntityBuilder()
        {
            _binaryImporterType = typeof(NpgsqlBinaryImporter);
            _cancellationTokenType = typeof(CancellationToken);
            _npgsqlDbType = typeof(NpgsqlDbType);

            _binaryImporterParameter = Expression.Parameter(_binaryImporterType, "binaryImporter");
            _cancellationTokenParameter = Expression.Parameter(_cancellationTokenType, "cancellationToken");
        }

        internal EntityBuilder()
        {
            ColumnData = new Dictionary<string, ColumnData<TEntity>>();
            IgnoredColumns = new List<string>();

            _entityType = typeof(TEntity);
            _entityParameter = Expression.Parameter(_entityType, "entity");

            _tableName = _entityType.Name + 's';
        }

        public EntityBuilder<TEntity> MapToTable(string tableName)
        {
            _tableName = tableName;

            return this;
        }

        public EntityBuilder<TEntity> MapToColumn(Expression<Func<TEntity, object>> customMapper, string columnName)
        {
            var property = GetTargetProperty(customMapper);

            if (this.ColumnData.TryGetValue(property.Name, out var columnData))
            {
                columnData.ColumnName = columnName;
            }
            else
            {
                this.ColumnData.Add(property.Name, new ColumnData<TEntity>(property) { ColumnName = columnName });
            }

            return this;
        }

        public EntityBuilder<TEntity> MapType(Expression<Func<TEntity, object>> customMapper, NpgsqlDbType npgsqlDbType)
        {
            var property = GetTargetProperty(customMapper);

            if (this.ColumnData.TryGetValue(property.Name, out var columnData))
            {
                columnData.NpgsqlDbType = npgsqlDbType;
            }
            else
            {
                this.ColumnData.Add(property.Name, new ColumnData<TEntity>(property) { NpgsqlDbType = npgsqlDbType });
            }

            return this;
        }

        public EntityBuilder<TEntity> MapIgnore(Expression<Func<TEntity, object>> customMapper)
        {
            var property = GetTargetProperty(customMapper);

            this.IgnoredColumns.Add(property.Name);

            return this;
        }

        public EntityBuilder<TEntity> MapOneToMany<TTarget, TKeyType>(Expression<Func<TEntity, IEnumerable<TTarget>>> customMapper, Expression<Func<TEntity, TKeyType>> primaryKeySelector, Expression<Func<TTarget, TKeyType>> foreignKeySelector) where TTarget : class
        {
            var property = GetTargetProperty(customMapper);
            var primaryProperty = GetTargetProperty(primaryKeySelector);
            var foreignProperty = GetTargetProperty(foreignKeySelector);

            var foreignType = typeof(TTarget);

            var foreignKeyParameter = Expression.Parameter(foreignType, "foreignColumn");

            var primaryKeyProperty = Expression.Property(_entityParameter, primaryProperty);
            var foreignKeyProperty = Expression.Property(foreignKeyParameter, foreignProperty);

            var assignment = Expression.Lambda<Action<TEntity, TTarget>>(Expression.Assign(foreignKeyProperty, primaryKeyProperty), _entityParameter, foreignKeyParameter);

            var entitiesParameter = Expression.Parameter(property.PropertyType, "entities");
            var connectionParameter = Expression.Parameter(typeof(NpgsqlConnection), "connection");

            var flatter = Expression.Call(typeof(EntityBuilder<TEntity>), "FlattenForeignColumns", new Type[] { foreignType }, entitiesParameter, customMapper, assignment);

            var valueWriter = Expression.Call(typeof(NpgsqlConnectionExtensions), "BulkInsertAsync", new Type[] { _entityType, foreignType }, connectionParameter, flatter, _cancellationTokenParameter);

            var lambda = Expression.Lambda<Func<IEnumerable<TEntity>, NpgsqlConnection, CancellationToken, Task<ulong>>>(valueWriter, entitiesParameter, connectionParameter, _cancellationTokenParameter).Compile();

            if (this.ColumnData.TryGetValue(property.Name, out var columnData))
            {
                columnData.ForeignColumnsWriter = lambda;
            }
            else
            {
                this.ColumnData.Add(property.Name, new ColumnData<TEntity>(property) { ForeignColumnsWriter = lambda });
            }

            return this;
        }

        public EntityBuilder<TEntity> MapGuidGenerator(Expression<Func<TEntity, Guid>> customMapper)
        {
            MapValueFactory(customMapper, (_, guid) => guid != Guid.Empty, _ => Guid.NewGuid());

            return this;
        }

        public EntityBuilder<TEntity> MapValueFactory<TTarget>(Expression<Func<TEntity, TTarget>> customMapper, Expression<Func<TEntity, TTarget, bool>> valueValidator, Expression<Func<TEntity, TTarget>> valueFactory)
        {
            var property = GetTargetProperty(customMapper);

            if (this.ColumnData.TryGetValue(property.Name, out var columnData))
            {
                columnData.ValueValidator = valueValidator;
                columnData.ValueFactory = valueFactory;
            }
            else
            {
                this.ColumnData.Add(property.Name, new ColumnData<TEntity>(property) { ValueFactory = valueFactory });
            }
            return this;
        }

        private PropertyInfo GetTargetProperty<TTarget, TReturnType>(Expression<Func<TTarget, TReturnType>> customMapper) where TTarget : class
        {
            var targetType = typeof(TTarget);

            var member = customMapper.Body as MemberExpression;
            if (member == null)
                throw new ArgumentException(string.Format(
                    "Expression '{0}' refers to a method, not a property.",
                    customMapper.ToString()));

            var propInfo = member.Member as PropertyInfo;

            if (propInfo == null)
                throw new ArgumentException(string.Format(
                    "Expression '{0}' refers to a field, not a property.",
                    customMapper.ToString()));

            if (targetType != propInfo.ReflectedType &&
                !targetType.IsSubclassOf(propInfo!.ReflectedType))
                throw new ArgumentException(string.Format(
                    "Expression '{0}' refers to a property that is not from type {1}.",
                    customMapper.ToString(),
                    targetType));

            return propInfo;
        }

        internal void Build()
        {
            var entityProperties = _entityType.GetProperties(BindingFlags.Public | BindingFlags.Instance);

            if (entityProperties.Length == 0)
                throw new TypeArgumentException("The specified type doesn't contain any public instance properties.");

            var columnDefinitions = new List<ColumnDefinition<TEntity>>();
            var foreignColumnDefinitions = new List<ForeignColumnDefinition<TEntity>>();

            foreach (var entityProperty in entityProperties)
            {
                if (entityProperty.CanWrite &&
                    !Attribute.IsDefined(entityProperty, typeof(NotMappedAttribute)) &&
                    !IgnoredColumns.Contains(entityProperty.Name))
                {
                    Expression lambdaBody = null!;

                    var isForeignColumn = false;

                    var entityPropertyExpression = Expression.Property(_entityParameter, entityProperty);

                    if (ColumnData.TryGetValue(entityProperty.Name, out var columnData))
                    {
                        var expressions = new List<Expression>();

                        if (columnData.ForeignColumnsWriter is { })
                        {
                            foreignColumnDefinitions.Add(new ForeignColumnDefinition<TEntity>(columnData.ForeignColumnsWriter));

                            isForeignColumn = true;
                        }
                        else
                        {
                            if (columnData.ValueFactory is { } && columnData.ValueValidator is { })
                            {
                                var validatorInvoke = Expression.Invoke(columnData.ValueValidator, _entityParameter, entityPropertyExpression);

                                var valueFactoryInvoke = Expression.Invoke(columnData.ValueFactory, _entityParameter);

                                var valueAssignment = Expression.Assign(entityPropertyExpression, valueFactoryInvoke);

                                var valueValidation = Expression.IfThen(Expression.IsFalse(validatorInvoke), valueAssignment);

                                expressions.Add(valueValidation);
                            }

                            if (columnData.NpgsqlDbType is { })
                            {
                                var dbTypeConstant = Expression.Constant(columnData.NpgsqlDbType, _npgsqlDbType);

                                expressions.Add(Expression.Call(_binaryImporterParameter, "WriteAsync", new Type[] { entityProperty.PropertyType }, entityPropertyExpression, dbTypeConstant, _cancellationTokenParameter));
                            }
                            else
                            {
                                expressions.Add(Expression.Call(_binaryImporterParameter, "WriteAsync", new Type[] { entityProperty.PropertyType }, entityPropertyExpression, _cancellationTokenParameter));
                            }

                            lambdaBody = Expression.Block(expressions);
                        }
                    }
                    else
                    {
                        lambdaBody = Expression.Call(_binaryImporterParameter, "WriteAsync", new Type[] { entityProperty.PropertyType }, entityPropertyExpression, _cancellationTokenParameter);
                    }

                    if (!isForeignColumn)
                    {
                        var valueWriterLambda = Expression.Lambda<Func<TEntity, NpgsqlBinaryImporter, CancellationToken, Task>>(lambdaBody, _entityParameter, _binaryImporterParameter, _cancellationTokenParameter).Compile();

                        columnDefinitions.Add(new ColumnDefinition<TEntity>(columnData?.ColumnName ?? entityProperty.Name, valueWriterLambda));
                    }
                }
            }

            EntityDefinitionCache.TryAddEntity(new EntityDefinition<TEntity>(_tableName, columnDefinitions, foreignColumnDefinitions), _entityType);
        }

        private static IEnumerable<TTarget> FlattenForeignColumns<TTarget>(IEnumerable<TEntity> entities, Func<TEntity, IEnumerable<TTarget>> foreignColumns, Action<TEntity, TTarget> valueCopier) where TTarget : class
        {
            foreach (var entity in entities)
            {
                foreach (var foreignColumn in foreignColumns.Invoke(entity))
                {
                    valueCopier.Invoke(entity, foreignColumn);

                    yield return foreignColumn;
                }
            }
        }
    }

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

    internal interface IEntityDefinition
    {
    }

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

    internal class ForeignColumnDefinition<TEntity> where TEntity : class
    {
        internal Func<IEnumerable<TEntity>, NpgsqlConnection, CancellationToken, Task<ulong>> WriteValues { get; }

        internal ForeignColumnDefinition(Func<IEnumerable<TEntity>, NpgsqlConnection, CancellationToken, Task<ulong>> valueWriter)
        {
            WriteValues = valueWriter;
        }
    }

    public abstract class EntityConfiguration<TEntity> where TEntity : class
    {
        protected abstract void Configure(EntityBuilder<TEntity> entityBuilder);

        internal void BuildConfiguration()
        {
            var entityBuilder = new EntityBuilder<TEntity>();

            Configure(entityBuilder);

            entityBuilder.Build();
        }
    }

    public static class EntityConfigurator
    {
        public static bool IsBuild { get; private set; }

        public static void BuildConfigurations()
        {
            BuildConfigurations(Assembly.GetCallingAssembly());

            IsBuild = true;
        }

        public static void BuildConfigurations(params Assembly[] assemblies)
        {
            foreach (var assembly in assemblies)
            {
                BuildConfigurations(assembly);
            }
        }

        public static void BuildConfigurations(Assembly assembly)
        {
            var entityConfigurationType = typeof(EntityConfiguration<>);

            var entityConfigurations = assembly.GetExportedTypes().Where(x => x.IsClass && x.IsSubclassOf(entityConfigurationType));

            foreach (var entityConfiguration in entityConfigurations)
            {
                var genericArgumentType = entityConfiguration.GetGenericArguments()[0];

                var genericConfigurationInstance = Activator.CreateInstance(genericArgumentType);

                var configurationMethod = genericArgumentType.GetMethod("BuildConfiguration", BindingFlags.NonPublic | BindingFlags.Instance);

                configurationMethod!.Invoke(genericConfigurationInstance, null);
            }
        }
    }
}
