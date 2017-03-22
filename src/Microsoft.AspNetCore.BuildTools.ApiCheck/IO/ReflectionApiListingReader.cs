﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using ApiCheck.Description;
using ApiCheck.Utilities;

namespace ApiCheck.IO
{
    public class ReflectionApiListingReader : IApiListingReader
    {
        private const BindingFlags SearchFlags = BindingFlags.Public |
            BindingFlags.NonPublic |
            BindingFlags.Instance |
            BindingFlags.Static |
            BindingFlags.DeclaredOnly;

        private readonly Assembly _assembly;
        private readonly IEnumerable<Func<MemberInfo, bool>> _filters;

        public ReflectionApiListingReader(Assembly assembly, IEnumerable<Func<MemberInfo, bool>> filters)
        {
            _assembly = assembly;
            _filters = filters ?? Enumerable.Empty<Func<MemberInfo, bool>>();
        }

        public ApiListing Read()
        {
            var types = _assembly.DefinedTypes
                .Where(t => t.IsPublic || t.IsNestedPublic || t.IsNestedFamily || t.IsNestedFamORAssem);

            var document = new ApiListing()
            {
                AssemblyIdentity = _assembly.GetName().ToString()
            };

            foreach (var type in types.Where(t => !_filters.Any(filter => filter(t))))
            {
                var ApiListingType = GenerateTypeDescriptor(type);
                document.Types.Add(ApiListingType);
            }

            return document;
        }

        public static TypeDescriptor GenerateTypeDescriptor(TypeInfo type, IEnumerable<Func<MemberInfo, bool>> filters = null)
        {
            filters = filters ?? Enumerable.Empty<Func<MemberInfo, bool>>();
            var generator = new ReflectionApiListingReader(type.Assembly, filters);
            return generator.GenerateTypeDescriptor(type);
        }

        private TypeDescriptor GenerateTypeDescriptor(TypeInfo type)
        {
            var typeDescriptor = new TypeDescriptor();

            typeDescriptor.Name = ReflectionHelper.GetTypeNameFor(type);

            typeDescriptor.Kind = GetTypeKind(type);

            if (typeDescriptor.Kind == TypeKind.Unknown)
            {
                throw new InvalidOperationException($"Can't determine type for {type.FullName}");
            }

            // At this point we've filtered away any non public or protected member,
            // so we only need to check if something is public
            typeDescriptor.Visibility = type.IsPublic || type.IsNestedPublic ? ApiElementVisibility.Public : ApiElementVisibility.Protected;

            typeDescriptor.Static = typeDescriptor.Kind == TypeKind.Class && type.IsSealed && type.IsAbstract;

            typeDescriptor.Abstract = type.IsAbstract;

            typeDescriptor.Sealed = type.IsSealed;

            if (type.BaseType != null &&
                type.BaseType != typeof(object) &&
                type.BaseType != typeof(ValueType) &&
                !(type.IsEnum && type.GetEnumUnderlyingType() == typeof(int)))
            {
                typeDescriptor.BaseType = !type.IsEnum ?
                    ReflectionHelper.GetTypeNameFor(type.BaseType.GetTypeInfo()) :
                    ReflectionHelper.GetTypeNameFor(type.GetEnumUnderlyingType().GetTypeInfo());
            }

            if (type.ImplementedInterfaces?.Count() > 0)
            {
                var interfaces = ReflectionHelper.GetImplementedInterfacesFor(type).ToList();
                foreach (var @interface in interfaces.Select(i => ReflectionHelper.GetTypeNameFor(i)))
                {
                    typeDescriptor.ImplementedInterfaces.Add(@interface);
                }
            }

            if (type.IsGenericType)
            {
                var constraints = GetGenericConstraintsFor(type.GetGenericArguments().Select(t => t.GetTypeInfo()).ToArray());
                foreach (var constraint in constraints)
                {
                    typeDescriptor.GenericParameters.Add(constraint);
                }
            }

            var members = type.GetMembers(SearchFlags);

            foreach (var member in members)
            {
                if (_filters.Any(f => f(member)))
                {
                    continue;
                }

                var memberApiListing = GenerateMemberApiListing(type, member);
                if (memberApiListing != null)
                {
                    typeDescriptor.Members.Add(memberApiListing);
                }
            }

            return typeDescriptor;
        }

        private static TypeKind GetTypeKind(TypeInfo type)
        {
            if (type.IsInterface)
            {
                return TypeKind.Interface;
            }

            if (type.IsEnum)
            {
                return TypeKind.Enumeration;
            }

            if (type.IsValueType)
            {
                return TypeKind.Struct;
            }

            if (type.IsClass)
            {
                return TypeKind.Class;
            }

            return TypeKind.Unknown;
        }

        private static IEnumerable<GenericParameterDescriptor> GetGenericConstraintsFor(TypeInfo[] genericArguments)
        {
            for (var i = 0; i < genericArguments.Length; i++)
            {
                var typeArgument = genericArguments[i];
                var constraintDescriptor = new GenericParameterDescriptor();

                if (typeArgument.IsGenericParameter)
                {
                    if (typeArgument.BaseType != null &&
                        typeArgument.BaseType != typeof(object)
                        && typeArgument.BaseType != typeof(ValueType))
                    {
                        constraintDescriptor.BaseTypeOrInterfaces.Add(ReflectionHelper.GetTypeNameFor(typeArgument.BaseType.GetTypeInfo()));
                    }

                    foreach (var interfaceType in ReflectionHelper.GetImplementedInterfacesFor(typeArgument))
                    {
                        constraintDescriptor.BaseTypeOrInterfaces.Add(ReflectionHelper.GetTypeNameFor(interfaceType));
                    }

                    constraintDescriptor.ParameterName = typeArgument.Name;
                    constraintDescriptor.ParameterPosition = typeArgument.GenericParameterPosition;
                    constraintDescriptor.New = (typeArgument.GenericParameterAttributes & GenericParameterAttributes.DefaultConstructorConstraint) == GenericParameterAttributes.DefaultConstructorConstraint;
                    constraintDescriptor.Class = (typeArgument.GenericParameterAttributes & GenericParameterAttributes.ReferenceTypeConstraint) == GenericParameterAttributes.ReferenceTypeConstraint;
                    constraintDescriptor.Struct = (typeArgument.GenericParameterAttributes & GenericParameterAttributes.NotNullableValueTypeConstraint) == GenericParameterAttributes.NotNullableValueTypeConstraint;
                }
                else
                {
                    constraintDescriptor.ParameterName = ReflectionHelper.GetTypeNameFor(typeArgument);
                }

                yield return constraintDescriptor;
            }
        }

        public static MemberDescriptor GenerateMemberApiListing(TypeInfo type, MemberInfo member)
        {
            switch (member.MemberType)
            {
                case MemberTypes.Constructor:
                    var ctor = (ConstructorInfo)member;
                    if (!ctor.IsPublic && !ctor.IsFamily && !ctor.IsFamilyOrAssembly)
                    {
                        return null;
                    }

                    var constructorDescriptor = new MemberDescriptor();
                    constructorDescriptor.Kind = MemberKind.Constructor;
                    constructorDescriptor.Visibility = ctor.IsPublic ? ApiElementVisibility.Public : ApiElementVisibility.Protected;

                    constructorDescriptor.Name = ReflectionHelper.GetMemberNameFor(ctor);
                    foreach (var parameter in ctor.GetParameters())
                    {
                        var parameterDescriptor = GenerateParameterDescriptor(parameter);
                        constructorDescriptor.Parameters.Add(parameterDescriptor);
                    }

                    return constructorDescriptor;
                case MemberTypes.Method:
                    var name = member.Name;
                    var method = (MethodInfo)member;
                    if (!method.IsPublic && !method.IsFamily && !method.IsFamilyOrAssembly)
                    {
                        return null;
                    }

                    var methodDescriptor = new MemberDescriptor();

                    methodDescriptor.Kind = MemberKind.Method;

                    methodDescriptor.Visibility = method.IsPublic ? ApiElementVisibility.Public : ApiElementVisibility.Protected;

                    if (!type.IsInterface)
                    {
                        methodDescriptor.ExplicitInterface = GetInterfaceImplementation(method, explicitImplementation: true);
                        methodDescriptor.ImplementedInterface = methodDescriptor.ExplicitInterface ?? GetInterfaceImplementation(method, explicitImplementation: false);
                    }
                    else
                    {
                        methodDescriptor.Visibility = null;
                    }

                    methodDescriptor.Name = ReflectionHelper.GetMemberNameFor(method);

                    if (method.IsGenericMethod)
                    {
                        var constraints = GetGenericConstraintsFor(method.GetGenericArguments().Select(t => t.GetTypeInfo()).ToArray());
                        foreach (var constraint in constraints)
                        {
                            methodDescriptor.GenericParameter.Add(constraint);
                        }
                    }

                    methodDescriptor.Static = method.IsStatic;
                    methodDescriptor.Sealed = method.IsFinal;
                    methodDescriptor.Virtual = !type.IsInterface && method.IsVirtual;
                    methodDescriptor.Override = !type.IsInterface && method.IsVirtual && method.GetBaseDefinition() != method;
                    methodDescriptor.Abstract = !type.IsInterface && method.IsAbstract;
                    methodDescriptor.New = !method.IsAbstract && !method.IsVirtual && method.IsHideBySig &&
                        method.DeclaringType.GetMember(method.Name).OfType<MethodInfo>()
                        .Where(m => SameSignature(m, method)).Count() > 1;
                    methodDescriptor.Extension = method.IsDefined(typeof(ExtensionAttribute), false);

                    foreach (var parameter in method.GetParameters())
                    {
                        var parameterDescriptor = GenerateParameterDescriptor(parameter);
                        methodDescriptor.Parameters.Add(parameterDescriptor);
                    }

                    methodDescriptor.ReturnType = ReflectionHelper.GetTypeNameFor(method.ReturnType.GetTypeInfo());

                    return methodDescriptor;
                case MemberTypes.Field:
                    var field = (FieldInfo)member;
                    if (!field.IsPublic && !field.IsFamily && !field.IsFamilyOrAssembly)
                    {
                        return null;
                    }

                    if (type.IsEnum && !field.IsLiteral)
                    {
                        // Skip storage for enumerations.
                        return null;
                    }

                    var fieldDescriptor = new MemberDescriptor();

                    fieldDescriptor.Visibility = field.IsPublic ? ApiElementVisibility.Public : ApiElementVisibility.Protected;

                    fieldDescriptor.Kind = MemberKind.Field;
                    fieldDescriptor.Name = field.Name;

                    if (type.IsEnum || field.IsLiteral)
                    {
                        fieldDescriptor.Literal = FormatLiteralValue(field.GetRawConstantValue(), field.FieldType);
                    }

                    if (type.IsEnum)
                    {
                        fieldDescriptor.Visibility = null;
                    }
                    else
                    {
                        fieldDescriptor.Constant = field.IsLiteral;
                        fieldDescriptor.Static = field.IsStatic;
                        fieldDescriptor.ReadOnly = field.IsInitOnly;
                        fieldDescriptor.ReturnType = ReflectionHelper.GetTypeNameFor(field.FieldType.GetTypeInfo());
                    }

                    return fieldDescriptor;
                case MemberTypes.Event:
                case MemberTypes.Property:
                case MemberTypes.NestedType:
                    // All these cases are covered by the methods they implicitly define on the class
                    // (Properties and Events) and when we enumerate all the types in an assembly (Nested types).
                    return null;
                case MemberTypes.TypeInfo:
                // There should not be any member passsed into this method that is not a top level type.
                case MemberTypes.Custom:
                // We don't know about custom member types, so better throw if we find something we don't understand.
                case MemberTypes.All:
                    throw new InvalidOperationException($"'{type.MemberType}' [{member}] is not supported.");
                default:
                    return null;
            }
        }
        private static string GetInterfaceImplementation(MethodInfo method, bool explicitImplementation)
        {
            var typeInfo = method.DeclaringType.GetTypeInfo();
            foreach (var interfaceImplementation in method.DeclaringType.GetInterfaces())
            {
                var map = typeInfo.GetRuntimeInterfaceMap(interfaceImplementation);
                if (map.TargetMethods.Any(m => m.Equals(method)))
                {
                    return !explicitImplementation || (method.IsPrivate && method.IsFinal) ?
                        ReflectionHelper.GetTypeNameFor(interfaceImplementation.GetTypeInfo()) :
                        null;
                }
            }

            return null;
        }

        private static bool SameSignature(MethodInfo candidate, MethodInfo method)
        {
            if (candidate.ReturnType != method.ReturnType)
            {
                return false;
            }

            var candidateParameters = candidate.GetParameters();
            var methodParameters = method.GetParameters();

            if (candidateParameters.Length != methodParameters.Length)
            {
                return false;
            }

            for (int i = 0; i < candidateParameters.Length; i++)
            {
                var candidateParameter = candidateParameters[i];
                var methodParameter = methodParameters[i];
                if (candidateParameter.ParameterType != methodParameter.ParameterType ||
                    candidateParameter.HasDefaultValue != methodParameter.HasDefaultValue ||
                    candidateParameter.IsIn != methodParameter.IsIn ||
                    candidateParameter.IsOut != methodParameter.IsOut ||
                    candidateParameter.IsOptional != methodParameter.IsOptional)
                {
                    return false;
                }
            }

            return true;
        }

        private static ParameterDescriptor GenerateParameterDescriptor(ParameterInfo parameter)
        {
            return new ParameterDescriptor
            {
                Name = parameter.Name,
                Type = ReflectionHelper.GetTypeNameFor(parameter.ParameterType.GetTypeInfo()),
                Direction = parameter.ParameterType.IsByRef && parameter.IsOut ? ParameterDirection.Out :
                    parameter.ParameterType.IsByRef && !parameter.IsOut ? ParameterDirection.Ref :
                    ParameterDirection.In,
                DefaultValue = parameter.HasDefaultValue ? FormatLiteralValue(parameter) : null,
                IsParams = parameter.GetCustomAttribute<ParamArrayAttribute>() != null
            };
        }

        private static string FormatLiteralValue(ParameterInfo parameter)
        {
            return FormatLiteralValue(parameter.RawDefaultValue, parameter.ParameterType);
        }

        private static string FormatLiteralValue(object rawDefaultValue, Type elementType)
        {
            if (rawDefaultValue == null)
            {
                var elementTypeInfo = elementType.GetTypeInfo();
                if (elementTypeInfo.IsValueType)
                {
                    return $"default({ReflectionHelper.GetTypeNameFor(elementTypeInfo)})";
                }

                return "null";
            }

            if (elementType == typeof(string))
            {
                return $"\"{rawDefaultValue}\"";
            }

            if (elementType == typeof(char))
            {
                return $"'{rawDefaultValue}'";
            }

            if (rawDefaultValue.GetType() == typeof(bool) ||
                rawDefaultValue.GetType() == typeof(byte) ||
                rawDefaultValue.GetType() == typeof(sbyte) ||
                rawDefaultValue.GetType() == typeof(short) ||
                rawDefaultValue.GetType() == typeof(ushort) ||
                rawDefaultValue.GetType() == typeof(int) ||
                rawDefaultValue.GetType() == typeof(uint) ||
                rawDefaultValue.GetType() == typeof(long) ||
                rawDefaultValue.GetType() == typeof(ulong) ||
                rawDefaultValue.GetType() == typeof(double) ||
                rawDefaultValue.GetType() == typeof(float) ||
                rawDefaultValue.GetType() == typeof(decimal))
            {
                return rawDefaultValue.ToString();
            }

            throw new InvalidOperationException("Unsupported default value type");
        }

        public void Dispose()
        {
        }
    }
}