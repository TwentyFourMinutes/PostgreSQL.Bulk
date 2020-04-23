namespace PostgreSQL.Bulk
{
    /// <summary>
    /// Used to configure a specific entity and its properties.
    /// </summary>
    /// <typeparam name="TEntity">The type of the entity to be configured.</typeparam>
    public abstract class EntityConfiguration<TEntity> where TEntity : class
    {
        /// <summary>
        /// Configures the actual entity and its properties.
        /// </summary>
        /// <param name="entityBuilder">The builder used to apply the configurations.</param>
        protected abstract void Configure(EntityBuilder<TEntity> entityBuilder);

        internal void BuildConfiguration()
        {
            var entityBuilder = new EntityBuilder<TEntity>();

            Configure(entityBuilder);

            entityBuilder.Build();
        }
    }
}
