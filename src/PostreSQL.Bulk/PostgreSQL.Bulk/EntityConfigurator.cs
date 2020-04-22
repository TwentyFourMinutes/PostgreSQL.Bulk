using System;
using System.Linq;
using System.Reflection;

namespace PostgreSQL.Bulk
{
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

            var entityConfigurations = assembly.GetExportedTypes().Where(x => x.IsClass && x.BaseType is { } && x.BaseType.IsGenericType && x.BaseType.GetGenericTypeDefinition() == entityConfigurationType);

            foreach (var entityConfiguration in entityConfigurations)
            {
                var genericConfigurationInstance = Activator.CreateInstance(entityConfiguration);

                var configurationMethod = entityConfiguration.GetMethod("BuildConfiguration", BindingFlags.NonPublic | BindingFlags.Instance);

                configurationMethod!.Invoke(genericConfigurationInstance, null);
            }
        }
    }
}
