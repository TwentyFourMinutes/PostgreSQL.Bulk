namespace PostgreSQL.Bulk
{
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
}
