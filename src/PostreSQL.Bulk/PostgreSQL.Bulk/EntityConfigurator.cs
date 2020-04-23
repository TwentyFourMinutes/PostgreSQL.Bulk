using System;
using System.Linq;
using System.Reflection;

namespace PostgreSQL.Bulk
{
    /// <summary>
    /// Provides access to methods which build the individual <see cref="EntityConfiguration{TEntity}"/>.
    /// </summary>
    public static class EntityConfigurator
    {
        internal static bool IsBuild { get; private set; }

        /// <summary>
        /// Builds all <see cref="EntityConfiguration{TEntity}"/>, which are declared in the calling assembly.
        /// </summary>
        public static void BuildConfigurations()
        {
            BuildConfigurations(Assembly.GetCallingAssembly());

            IsBuild = true;
        }

        /// <summary>
        /// Builds all <see cref="EntityConfiguration{TEntity}"/> in the passed assemblies.
        /// </summary>
        /// <param name="assemblies">The assemblies which contain the configurations.</param>
        public static void BuildConfigurations(params Assembly[] assemblies)
        {
            foreach (var assembly in assemblies)
            {
                BuildConfigurations(assembly);
            }

            IsBuild = true;
        }

        /// <summary>
        /// Builds all <see cref="EntityConfiguration{TEntity}"/> in the passed assembly.
        /// </summary>
        /// <param name="assembly">The assembly which contains the configurations.</param>
        public static void BuildConfigurations(Assembly assembly)
        {
            var entityConfigurationType = typeof(EntityConfiguration<>);

            var entityConfigurations = assembly.GetExportedTypes().Where(x => x.IsClass && x.BaseType is { } && x.BaseType.IsGenericType && x.BaseType.GetGenericTypeDefinition() == entityConfigurationType);

            foreach (var entityConfiguration in entityConfigurations)
            {
                var genericConfigurationInstance = Activator.CreateInstance(entityConfiguration);

                var configurationMethod = entityConfiguration.GetMethod("BuildConfiguration", BindingFlags.NonPublic | BindingFlags.Instance);

                configurationMethod!.Invoke(genericConfigurationInstance, null);
            }
        }

        /// <summary>
        /// Builds a specific <see cref="EntityConfiguration{TEntity}"/>.
        /// </summary>
        /// <typeparam name="TConfiguration">The <see cref="EntityConfiguration{TEntity}"/> which should be build.</typeparam>
        /// <typeparam name="TConfigurationType">The type which the <see cref="EntityConfiguration{TEntity}"/> builds.</typeparam>
        public static void BuildConfiguration<TConfiguration, TConfigurationType>() where TConfiguration : EntityConfiguration<TConfigurationType>, new()
                                                                                    where TConfigurationType : class
        {
            new TConfiguration().BuildConfiguration();

            IsBuild = true;
        }
    }
}
