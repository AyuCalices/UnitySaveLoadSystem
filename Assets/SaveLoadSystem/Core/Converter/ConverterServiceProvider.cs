using System;
using System.Collections.Generic;
using System.Reflection;
using SaveLoadSystem.Core.Converter.Collections;

namespace SaveLoadSystem.Core.Converter
{
    public static class ConverterServiceProvider
    {
        private static readonly HashSet<(Type Type, Type HandledType)> UsableConverterLookup = new();
        private static readonly Dictionary<Type, IConverter> CreatedConverterLookup = new();

        static ConverterServiceProvider()
        {
            // Register all types that inherit from SaveMateBaseConverter<T>
            var allTypes = Assembly.GetExecutingAssembly().GetTypes();

            foreach (var type in allTypes)
            {
                if (type.IsAbstract || type.IsInterface) continue;

                // Check if the type inherits from SaveMateBaseConverter<T>
                var baseType = type.BaseType;
                while (baseType != null)
                {
                    if (baseType.IsGenericType && baseType.GetGenericTypeDefinition() == typeof(BaseConverter<>))
                    {
                        // Handle both open and non-generic types
                        var handledType = baseType.GetGenericArguments()[0];

                        if (handledType.IsGenericType)
                        {
                            // Store open generic type definition (e.g., List<>)
                            handledType = handledType.GetGenericTypeDefinition();
                        }
                        
                        UsableConverterLookup.Add((type, handledType));
                        break;
                    }
                    baseType = baseType.BaseType;
                }
            }
        }
        
        public static bool ExistsAndCreate<T>()
        {
            return ExistsAndCreate(typeof(T));
        }

        public static bool ExistsAndCreate(Type type)
        {
            // Check if the converter already exists in the lookup
            if (CreatedConverterLookup.ContainsKey(type))
            {
                return true;
            }

            // Discover the converter type
            var converterType = FindConverterType(type);

            if (converterType == null)
            {
                return false;
            }

            // Create the converter dynamically and cache it
            var instance = (IConverter)Activator.CreateInstance(converterType);
            CreatedConverterLookup[type] = instance;
            return true;
        }

        public static IConverter GetConverter<T>()
        {
            return GetConverter(typeof(T));
        }

        public static IConverter GetConverter(Type type)
        {
            // Check if the converter already exists in the lookup
            if (CreatedConverterLookup.TryGetValue(type, out var converter))
            {
                return converter;
            }

            // Discover the converter type
            var converterType = FindConverterType(type);

            if (converterType == null)
            {
                throw new NotSupportedException($"No converter found or supported for type {type.FullName}");
            }

            // Create the converter dynamically and cache it
            var instance = (IConverter)Activator.CreateInstance(converterType);
            CreatedConverterLookup[type] = instance;
            return instance;
        }

        private static Type FindConverterType(Type targetType)
        {
            // Handle array types specifically (for any dimension)
            if (targetType.IsArray)
            {
                return typeof(ArrayConverter<>).MakeGenericType(targetType);
            }

            // Handle all other types
            foreach (var (converterType, handledType) in UsableConverterLookup)
            {
                // Match exact types
                if (handledType == targetType)
                {
                    return converterType;
                }

                // Match open generic types
                if (handledType.IsGenericTypeDefinition && targetType.IsGenericType &&
                    handledType == targetType.GetGenericTypeDefinition())
                {
                    return converterType.MakeGenericType(targetType.GetGenericArguments());
                }
            }

            return null; // No matching converter found
        }
    }

    public abstract class BaseConverter<T> : IConverter
    {
        public void Save(object input, SaveDataHandler saveDataHandler)
        {
            OnSave((T)input, saveDataHandler);
        }

        protected abstract void OnSave(T input, SaveDataHandler saveDataHandler);
        
        public object CreateInstanceForLoad(LoadDataHandler loadDataHandler)
        {
            return OnCreateInstanceForLoad(loadDataHandler);
        }

        protected abstract T OnCreateInstanceForLoad(LoadDataHandler loadDataHandler);

        public void Load(object input, LoadDataHandler loadDataHandler)
        {
            OnLoad((T)input, loadDataHandler);
        }

        protected abstract void OnLoad(T input, LoadDataHandler loadDataHandler);
    }

    public interface IConverter
    {
        void Save(object input, SaveDataHandler saveDataHandler);
        object CreateInstanceForLoad(LoadDataHandler loadDataHandler);
        void Load(object input, LoadDataHandler loadDataHandler);
    }
}
