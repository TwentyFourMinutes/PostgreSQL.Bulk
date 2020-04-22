using Npgsql;
using NpgsqlTypes;
using System;
using System.Collections;
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
        internal Dictionary<string, ColumnData> ColumnData { get; }
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
            ColumnData = new Dictionary<string, ColumnData>();
            IgnoredColumns = new List<string>();

            _entityType = typeof(TEntity);
            _entityParameter = Expression.Parameter(_entityType, "entity");

            _tableName = _entityType.Name + 's';
        }

        public EntityBuilder<TEntity> MapTableName(string tableName)
        {
            _tableName = tableName;

            return this;
        }

        public EntityBuilder<TEntity> MapColumnName(Expression<Func<TEntity, object>> customMapper, string columnName)
        {
            var property = GetTargetProperty(customMapper);

            if (this.ColumnData.TryGetValue(property.Name, out var columnData))
            {
                columnData.ColumnName = columnName;
            }
            else
            {
                this.ColumnData.Add(property.Name, new ColumnData(property) { ColumnName = columnName });
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
                this.ColumnData.Add(property.Name, new ColumnData(property) { NpgsqlDbType = npgsqlDbType });
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
            var pkProperty = GetTargetProperty(primaryKeySelector);
            var foreignProperty = GetTargetProperty(foreignKeySelector);

            var targetsProperty = Expression.Property(_entityParameter, property);
            var primaryKeyProperty = Expression.Property(_entityParameter, pkProperty);

            var enumerator = Expression.Variable(typeof(IEnumerator<TTarget>), "enumerator");

            var getEnumerator = Expression.Call(targetsProperty, property.PropertyType.GetMethod("GetEnumerator"));
            var disposeEnumerator = Expression.Call(enumerator, typeof(IDisposable).GetMethod("Dispose"));

            var enumeratorAssignment = Expression.Assign(enumerator, getEnumerator);

            var breakLabel = Expression.Label("break");

            var block = Expression.Block(variables: new ParameterExpression[] { enumerator },
                                         enumeratorAssignment,
                                         Expression.Loop(
                                         Expression.IfThenElse(
                                             Expression.IsTrue(Expression.Call(enumerator, typeof(IEnumerator).GetMethod("MoveNext"))),
                                             Expression.Assign(
                                                Expression.Property(Expression.Property(enumerator, "Current"), foreignProperty),
                                                primaryKeyProperty),
                                             Expression.Break(breakLabel)),
                                         breakLabel),
                                         disposeEnumerator);

            var checkForValue = Expression.IfThen(Expression.NotEqual(targetsProperty, Expression.Constant(null)),
                                                  block);

            if (this.ColumnData.TryGetValue(property.Name, out var columnData))
            {
                columnData.OneToManyCopier = checkForValue;
            }
            else
            {
                this.ColumnData.Add(property.Name, new ColumnData(property) { OneToManyCopier = checkForValue });
            }

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
                this.ColumnData.Add(property.Name, new ColumnData(property) { ValueFactory = valueFactory });
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
            var foreignColumnDefinitions = new List<ColumnDefinition<TEntity>>();

            foreach (var entityProperty in entityProperties)
            {
                if (entityProperty.CanWrite &&
                    !Attribute.IsDefined(entityProperty, typeof(NotMappedAttribute)) &&
                    !IgnoredColumns.Contains(entityProperty.Name))
                {
                    Expression lambdaBody;

                    var isForeignColumn = false;

                    var entityPropertyExpression = Expression.Property(_entityParameter, entityProperty);

                    if (ColumnData.TryGetValue(entityProperty.Name, out var columnData))
                    {
                        var expressions = new List<Expression>();

                        if (columnData.OneToManyCopier is { })
                        {
                            expressions.Add(columnData.OneToManyCopier);

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
                        }

                        lambdaBody = Expression.Block(expressions);
                    }
                    else
                    {
                        lambdaBody = Expression.Call(_binaryImporterParameter, "WriteAsync", new Type[] { entityProperty.PropertyType }, entityPropertyExpression, _cancellationTokenParameter);
                    }

                    var valueWriterLambda = Expression.Lambda<Func<TEntity, NpgsqlBinaryImporter, CancellationToken, Task>>(lambdaBody, _entityParameter, _binaryImporterParameter, _cancellationTokenParameter).Compile();

                    if (isForeignColumn)
                    {
                        foreignColumnDefinitions.Add(new ColumnDefinition<TEntity>(columnData?.ColumnName ?? entityProperty.Name, valueWriterLambda));
                    }
                    else
                    {
                        columnDefinitions.Add(new ColumnDefinition<TEntity>(columnData?.ColumnName ?? entityProperty.Name, valueWriterLambda));
                    }
                }
            }

            EntityDefinitionCache.TryAddEntity(new EntityDefinition<TEntity>(_tableName, columnDefinitions, foreignColumnDefinitions), _entityType);
        }

        private
    }

    internal class ColumnData
    {
        internal string ColumnName { get; set; }

        internal NpgsqlDbType? NpgsqlDbType { get; set; }

        internal LambdaExpression? ValueFactory { get; set; }
        internal LambdaExpression? ValueValidator { get; set; }

        internal Expression? OneToManyCopier { get; set; }

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

        internal ReadOnlyCollection<ColumnDefinition<TEntity>> ForeignColumnDefinitions { get; }

        internal EntityDefinition(string tableName, List<ColumnDefinition<TEntity>> columnDefinitions, List<ColumnDefinition<TEntity>> foreignColumnDefinitions)
        {
            TableName = tableName;
            ColumnDefinitions = new ReadOnlyCollection<ColumnDefinition<TEntity>>(columnDefinitions);
            ForeignColumnDefinitions = new ReadOnlyCollection<ColumnDefinition<TEntity>>(foreignColumnDefinitions);
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
        internal Task<long> WriteValues(NpgsqlConnection connection, IEnumerable<TEntity> entities, CancellationToken cancellationToken)
        {

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
