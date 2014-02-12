﻿// Copyright (c) Microsoft Corporation
// All rights reserved.
// 
// Licensed under the Apache License, Version 2.0 (the "License"); you may not
// use this file except in compliance with the License.  You may obtain a copy
// of the License at http://www.apache.org/licenses/LICENSE-2.0
// 
// THIS CODE IS PROVIDED *AS IS* BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY
// KIND, EITHER EXPRESS OR IMPLIED, INCLUDING WITHOUT LIMITATION ANY IMPLIED
// WARRANTIES OR CONDITIONS OF TITLE, FITNESS FOR A PARTICULAR PURPOSE,
// MERCHANTABLITY OR NON-INFRINGEMENT.
// 
// See the Apache Version 2.0 License for specific language governing
// permissions and limitations under the License.
namespace Microsoft.Hadoop.Avro
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using System.Runtime.CompilerServices;
    using System.Runtime.Serialization;
    using System.Text.RegularExpressions;

    internal static class TypeExtensions
    {
        /// <summary>
        ///     Checks if type t has a public parameter-less constructor.
        /// </summary>
        /// <param name="type">The type.</param>
        /// <returns>True if type t has a public parameter-less constructor, false otherwise.</returns>
        public static bool HasParameterlessConstructor(this Type type)
        {
            return type.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null) != null;
        }

        /// <summary>
        ///     Determines whether the type is definitely unsupported for schema generation.
        /// </summary>
        /// <param name="type">The type.</param>
        /// <returns>
        ///     <c>true</c> if the type is unsupported; otherwise, <c>false</c>.
        /// </returns>
        public static bool IsUnsupported(this Type type)
        {
            return type == typeof(IntPtr)
                || type == typeof(UIntPtr)
                || type == typeof(object)
                || type.ContainsGenericParameters
                || (!type.IsArray
                && !type.IsValueType
                && !type.IsAnonymous()
                && !type.HasParameterlessConstructor()
                && type != typeof(string)
                && type != typeof(Uri)
                && !type.IsAbstract
                && !type.IsInterface
                && !(type.IsGenericType && SupportedInterfaces.Contains(type.GetGenericTypeDefinition())));
        }

        /// <summary>
        /// The natively supported types.
        /// </summary>
        private static readonly HashSet<Type> NativelySupported = new HashSet<Type>
        {
            typeof(char),
            typeof(byte),
            typeof(sbyte),
            typeof(short),
            typeof(ushort),
            typeof(uint),
            typeof(int),
            typeof(bool),
            typeof(long),
            typeof(ulong),
            typeof(float),
            typeof(double),
            typeof(decimal),
            typeof(string),
            typeof(Uri),
            typeof(byte[]),
            typeof(DateTime),
            typeof(DateTimeOffset),
            typeof(Guid)
        };

        public static bool IsNativelySupported(this Type type)
        {
            var notNullable = Nullable.GetUnderlyingType(type) ?? type;
            return NativelySupported.Contains(notNullable)
                || type.IsArray
                || type.IsKeyValuePair()
                || type.GetAllInterfaces()
                       .FirstOrDefault(t => t.IsGenericType && 
                                            t.GetGenericTypeDefinition() == typeof(IEnumerable<>)) != null;
        }

        private static readonly HashSet<Type> SupportedInterfaces = new HashSet<Type>
        {
            typeof(IList<>),
            typeof(IDictionary<,>)
        };

        public static bool IsAnonymous(this Type type)
        {
            return type.FullName.Contains("__Anonymous");
        }

        public static PropertyInfo GetPropertyByName(
            this Type type, string name, BindingFlags flags = BindingFlags.Public | BindingFlags.FlattenHierarchy | BindingFlags.Instance)
        {
            if (type.IsInterface)
            {
                var considered = new HashSet<Type>();
                var queue = new Queue<Type>();

                considered.Add(type);
                queue.Enqueue(type);
                while (queue.Count > 0)
                {
                    Type subType = queue.Dequeue();
                    foreach (Type subInterface in subType.GetInterfaces())
                    {
                        if (considered.Contains(subInterface))
                        {
                            continue;
                        }

                        considered.Add(subInterface);
                        queue.Enqueue(subInterface);
                    }

                    PropertyInfo property = subType.GetProperty(name, flags);

                    if (property != null)
                    {
                        return property;
                    }
                }

                return null;
            }

            return type.GetProperty(name, flags);
        }

        public static MethodInfo GetMethodByName(this Type type, string shortName, params Type[] arguments)
        {
            var result = type
                .GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .SingleOrDefault(m => m.Name == shortName && m.GetParameters().Select(p => p.ParameterType).SequenceEqual(arguments));

            if (result != null)
            {
                return result;
            }

            return
                type
                .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                .FirstOrDefault(m => (m.Name.EndsWith(shortName, StringComparison.Ordinal) ||
                                       m.Name.EndsWith("." + shortName, StringComparison.Ordinal))
                                 && m.GetParameters().Select(p => p.ParameterType).SequenceEqual(arguments));
        }

        /// <summary>
        /// Gets all fields of the type.
        /// </summary>
        /// <param name="t">The type.</param>
        /// <returns>Collection of fields.</returns>
        public static IEnumerable<FieldInfo> GetAllFields(this Type t)
        {
            if (t == null)
            {
                return Enumerable.Empty<FieldInfo>();
            }

            const BindingFlags Flags = 
                BindingFlags.Public |
                BindingFlags.NonPublic |
                BindingFlags.Instance |
                BindingFlags.DeclaredOnly;
            return t
                .GetFields(Flags)
                .Where(f => !f.IsDefined(typeof(CompilerGeneratedAttribute), false))
                .Concat(GetAllFields(t.BaseType));
        }

        /// <summary>
        /// Gets all properties of the type.
        /// </summary>
        /// <param name="t">The type.</param>
        /// <returns>Collection of properties.</returns>
        public static IEnumerable<PropertyInfo> GetAllProperties(this Type t)
        {
            if (t == null)
            {
                return Enumerable.Empty<PropertyInfo>();
            }

            const BindingFlags Flags =
                BindingFlags.Public |
                BindingFlags.NonPublic |
                BindingFlags.Instance |
                BindingFlags.DeclaredOnly;
            return t
                .GetProperties(Flags)
                .Where(p => !p.IsDefined(typeof(CompilerGeneratedAttribute), false)
                            && p.GetIndexParameters().Length == 0)
                .Concat(GetAllProperties(t.BaseType));
        }

        public static IEnumerable<Type> GetAllInterfaces(this Type t)
        {
            if (t.IsInterface)
            {
                yield return t;
            }

            foreach (var i in t.GetInterfaces())
            {
                yield return i;
            }
        }

        public static string GetStrippedFullName(this Type type)
        {
            if (type == null)
            {
                throw new ArgumentNullException("type");
            }

            if (string.IsNullOrEmpty(type.Namespace))
            {
                return StripAvroNonCompatibleCharacters(type.Name);
            }

            return StripAvroNonCompatibleCharacters(type.Namespace + "." + type.Name);
        }

        public static string StripAvroNonCompatibleCharacters(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return value;
            }

            return Regex.Replace(value, @"[^A-Za-z0-9_\.]", string.Empty, RegexOptions.None);
        }

        public static bool IsFlagEnum(this Type type)
        {
            if (type == null)
            {
                throw new ArgumentNullException("type");
            }
            return type.GetCustomAttributes(false).ToList().Find(a => a is FlagsAttribute) != null;
        }

        public static bool CanContainNull(this Type type)
        {
            var underlyingType = Nullable.GetUnderlyingType(type);
            return !type.IsValueType || underlyingType != null;
        }

        public static bool IsKeyValuePair(this Type type)
        {
            return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(KeyValuePair<,>);
        }

        public static bool CanBeKnownTypeOf(this Type type, Type baseType)
        {
            return !type.IsAbstract
                && (type.IsSubclassOf(baseType)
                    || type == baseType
                    || (baseType.IsInterface && baseType.IsAssignableFrom(type)));
        }

        public static IEnumerable<Type> GetAllKnownTypes(this Type t)
        {
            if (t == null)
            {
                return Enumerable.Empty<Type>();
            }

            return t.GetCustomAttributes(false)
                .OfType<KnownTypeAttribute>()
                .Select(a => a.Type)
                .Concat(GetAllKnownTypes(t.BaseType));
        }
    }
}
