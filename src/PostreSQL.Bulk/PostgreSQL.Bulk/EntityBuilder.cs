using Npgsql;
using NpgsqlTypes;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace PostgreSQL.Bulk
{
    /// <summary>
    /// Provides access to methods which allow the configuration of each property inside an entity. Also allows for the configuration of the entity itself.
    /// </summary>
    /// <typeparam name="TEntity">The type of the entity which should be configured.</typeparam>
    public class EntityBuilder<TEntity> where TEntity : class
    {
        internal Dictionary<string, ColumnData<TEntity>> ColumnData { get; }
        internal List<string> IgnoredColumns { get; }

        private readonly Type _binaryImporterType;
        private readonly Type _cancellationTokenType;
        private readonly Type _npgsqlDbType;

        private readonly Type _entityType;

        private readonly ParameterExpression _binaryImporterParameter;
        private readonly ParameterExpression _cancellationTokenParameter;

        private readonly ParameterExpression _entityParameter;

        private string _tableName;

        internal EntityBuilder()
        {
            ColumnData = new Dictionary<string, ColumnData<TEntity>>();
            IgnoredColumns = new List<string>();

            _entityType = typeof(TEntity);
            _entityParameter = Expression.Parameter(_entityType, "entity");
            _tableName = string.Empty;

            _binaryImporterType = typeof(NpgsqlBinaryImporter);
            _cancellationTokenType = typeof(CancellationToken);
            _npgsqlDbType = typeof(NpgsqlDbType);

            _binaryImporterParameter = Expression.Parameter(_binaryImporterType, "binaryImporter");
            _cancellationTokenParameter = Expression.Parameter(_cancellationTokenType, "cancellationToken");
        }

        /// <summary>
        /// Maps the Entity to a specific table by its name.
        /// </summary>
        /// <param name="tableName">The table name to which the entity should map.</param>
        /// <returns>The <see cref="EntityConfiguration{TEntity}"/> which allows for further configuration of the entity.</returns>
        /// <remarks>
        /// If you don't call this method, it will be assumed that the name of your table is the same as the name of the entity suffixed by a 's'.
        /// You can also configure this, by applying the <see cref="TableAttribute"/> to your entity.
        /// </remarks>
        public EntityBuilder<TEntity> MapToTable(string tableName)
        {
            _tableName = tableName;

            return this;
        }

        /// <summary>
        /// Maps the specified property to the specific column by its name.
        /// </summary>
        /// <param name="customMapper">A lambda expression representing the property on the current type.</param>
        /// <param name="columnName">The column name to which the property should map.</param>
        /// <returns>The <see cref="EntityConfiguration{TEntity}"/> which allows for further configuration of the entity.</returns>
        /// <remarks>
        /// If you don't call this method, it will be assumed that the name of the column for this property is the same as the name of the property itself.
        /// You can also configure this, by applying the <see cref="ColumnAttribute"/> to your property.
        /// </remarks>
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

        /// <summary>
        /// Maps the specified property to a specific column type.
        /// </summary>
        /// <param name="customMapper">A lambda expression representing the property on the current type.</param>
        /// <param name="npgsqlDbType">The column type of the property.</param>
        /// <returns>The <see cref="EntityConfiguration{TEntity}"/> which allows for further configuration of the entity.</returns>
        /// <remarks>
        /// If you don't call this method, Npgsql will assume the type of the object on its own.
        /// </remarks>
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

        /// <summary>
        /// Ignores the specified property, by default all public properties which have a getter and a setter will be included.
        /// </summary>
        /// <param name="customMapper">A lambda expression representing the property on the current type.</param>
        /// <returns>The <see cref="EntityConfiguration{TEntity}"/> which allows for further configuration of the entity.</returns>
        /// <remarks>
        /// You can also configure this, by applying the <see cref="NotMappedAttribute"/> to your property.
        /// </remarks>
        public EntityBuilder<TEntity> Ignore(Expression<Func<TEntity, object>> customMapper)
        {
            var property = GetTargetProperty(customMapper);

            this.IgnoredColumns.Add(property.Name);

            return this;
        }

        /// <summary>
        /// Maps a one to many relation between two classes. This method will automatically populates the foreign key/>.
        /// </summary>
        /// <param name="customMapper">A lambda expression representing the collection navigation property on this entity type that represents the relationship (blog => blog.Posts).</param>
        /// <param name="primaryKeySelector">A lambda expression representing the primary key of the current type. The value of this property will be used to populate the foreign key.</param>
        /// <param name="foreignKeySelector">A lambda expression representing the foreign key of the navigation type. This property will be populated by the <paramref name="primaryKeySelector"/>.</param>
        /// <returns>The <see cref="EntityConfiguration{TEntity}"/> which allows for further configuration of the entity.</returns>
        /// <remarks>This method is compatible with <see cref="MapValueFactory{TTarget}(Expression{Func{TEntity, TTarget}}, Expression{Func{TEntity, TTarget, bool}}, Expression{Func{TEntity, TTarget}})"/> and <see cref="MapGuidGenerator(Expression{Func{TEntity, Guid}})"/>.</remarks>
        public EntityBuilder<TEntity> MapOneToMany<TTarget, TKeyType>(Expression<Func<TEntity, IEnumerable<TTarget>?>> customMapper, Expression<Func<TEntity, TKeyType>> primaryKeySelector, Expression<Func<TTarget, TKeyType>> foreignKeySelector) where TTarget : class
        {
            var property = GetTargetProperty(customMapper);

            return MapRelation(property, customMapper, primaryKeySelector, foreignKeySelector);
        }

        /// <summary>
        /// Maps a one to one relation between two classes. This method will automatically populates the foreign key/>.
        /// </summary>
        /// <param name="customMapper">A lambda expression representing the collection navigation property on this entity type that represents the relationship (blog => blog.Posts).</param>
        /// <param name="primaryKeySelector">A lambda expression representing the primary key of the current type. The value of this property will be used to populate the foreign key.</param>
        /// <param name="foreignKeySelector">A lambda expression representing the foreign key of the navigation type. This property will be populated by the <paramref name="primaryKeySelector"/>.</param>
        /// <returns>The <see cref="EntityConfiguration{TEntity}"/> which allows for further configuration of the entity.</returns>
        /// <remarks>This method is compatible with <see cref="MapValueFactory{TTarget}(Expression{Func{TEntity, TTarget}}, Expression{Func{TEntity, TTarget, bool}}, Expression{Func{TEntity, TTarget}})"/> and <see cref="MapGuidGenerator(Expression{Func{TEntity, Guid}})"/>.</remarks>
        public EntityBuilder<TEntity> MapOneToOne<TTarget, TKeyType>(Expression<Func<TEntity, TTarget?>> customMapper, Expression<Func<TEntity, TKeyType>> primaryKeySelector, Expression<Func<TTarget, TKeyType>> foreignKeySelector) where TTarget : class
        {
            var property = GetTargetProperty(customMapper);

            return MapRelation(property, customMapper, primaryKeySelector, foreignKeySelector);
        }

        private EntityBuilder<TEntity> MapRelation<TTarget, TKeyType>(PropertyInfo property, Expression customMapper, Expression<Func<TEntity, TKeyType>> primaryKeySelector, Expression<Func<TTarget, TKeyType>> foreignKeySelector) where TTarget : class
        {
            var primaryProperty = GetTargetProperty(primaryKeySelector);
            var foreignProperty = GetTargetProperty(foreignKeySelector);

            var foreignType = typeof(TTarget);

            var foreignKeyParameter = Expression.Parameter(foreignType, "foreignColumn");

            var primaryKeyProperty = Expression.Property(_entityParameter, primaryProperty);
            var foreignKeyProperty = Expression.Property(foreignKeyParameter, foreignProperty);

            var assignment = Expression.Lambda<Action<TEntity, TTarget>>(Expression.Assign(foreignKeyProperty, primaryKeyProperty), _entityParameter, foreignKeyParameter);

            var entitiesParameter = Expression.Parameter(typeof(IEnumerable<TEntity>), "entities");
            var connectionParameter = Expression.Parameter(typeof(NpgsqlConnection), "connection");

            var flatter = Expression.Call(typeof(EntityBuilderHelpers), "FlattenForeignColumns", new Type[] { _entityType, foreignType }, entitiesParameter, customMapper, assignment);

            var valueWriter = Expression.Call(typeof(NpgsqlConnectionExtensions), "BulkInsertAsync", new Type[] { foreignType }, connectionParameter, flatter, _cancellationTokenParameter);

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

        /// <summary>
        /// Maps a <see cref="Guid"/> generator to a property, which will be called before an instance with this column gets inserted. The factory will only be called, if the current value of the <see cref="Guid"/> equals to <see cref="Guid.Empty"/>.
        /// </summary>
        /// <param name="customMapper">A lambda expression representing the property on the current type.</param>
        /// <returns>The <see cref="EntityConfiguration{TEntity}"/> which allows for further configuration of the entity.</returns>
        public EntityBuilder<TEntity> MapGuidGenerator(Expression<Func<TEntity, Guid>> customMapper)
        {
            MapValueFactory(customMapper, (_, guid) => guid != Guid.Empty, _ => Guid.NewGuid());

            return this;
        }

        /// <summary>
        /// Maps a value factory to a property, which will be called before an instance with this column gets inserted.
        /// </summary>
        /// <param name="customMapper">A lambda expression representing the property on the current type.</param>
        /// <param name="valueValidator">A lambda expression returning a <see cref="bool"/>, whether or not the state of the type property is valid. If this lambda returns <see langword="false"/>, the <paramref name="valueFactory"/> will be called.</param>
        /// <param name="valueFactory">A lambda expression returning a value, which should be used for this property if the current value is not valid.</param>
        /// <returns>The <see cref="EntityConfiguration{TEntity}"/> which allows for further configuration of the entity.</returns>
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
                this.ColumnData.Add(property.Name, new ColumnData<TEntity>(property) { ValueValidator = valueValidator, ValueFactory = valueFactory });
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
                !targetType.IsSubclassOf(propInfo.ReflectedType!))
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

            if (string.IsNullOrEmpty(_tableName))
            {
                var tableAttribute = _entityType.GetCustomAttribute<TableAttribute>();

                if (tableAttribute is { })
                {
                    _tableName = tableAttribute.Name;
                }
                else
                {
                    _tableName = _entityType.Name + 's';
                }
            }

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

                        string columnName;

                        if (columnData is null || string.IsNullOrEmpty(columnData.ColumnName))
                        {
                            var columnAttribute = entityProperty.GetCustomAttribute<ColumnAttribute>();

                            if (columnAttribute is { })
                            {
                                columnName = columnAttribute.Name;
                            }
                            else
                            {
                                columnName = entityProperty.Name;
                            }
                        }
                        else
                        {
                            columnName = columnData.ColumnName;
                        }

                        columnDefinitions.Add(new ColumnDefinition<TEntity>(columnName, valueWriterLambda));
                    }
                }
            }

            EntityDefinitionCache.TryAddEntity(new EntityDefinition<TEntity>(_tableName, columnDefinitions, foreignColumnDefinitions), _entityType);
        }
    }
}