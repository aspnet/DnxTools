﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Mono.Cecil;
using Mono.Collections.Generic;
using NuGet.Packaging;

namespace NuGetPackageVerifier.Rules
{
    public class AssemblyHasCopyrightAttributeRule : AssemblyHasAttributeRuleBase
    {
        public override IEnumerable<PackageVerifierIssue> ValidateAttribute(
            string currentFilePath,
            AssemblyDefinition assembly,
            Collection<CustomAttribute> assemblyAttributes)
        {
            if (!HasCopyrightAttribute(assemblyAttributes))
            {
                yield return PackageIssueFactory.AssemblyMissingCopyrightAttribute(currentFilePath);
            }
        }

        private static bool HasCopyrightAttribute(Collection<CustomAttribute> asmAttrs)
        {
            var foundAttr = asmAttrs.SingleOrDefault(attr => attr.AttributeType.FullName == typeof(AssemblyCopyrightAttribute).FullName);
            if (foundAttr == null)
            {
                return false;
            }
            var foundAttrArg = foundAttr.ConstructorArguments.SingleOrDefault();
            var attrValue = foundAttrArg.Value as string;

            return !string.IsNullOrEmpty(attrValue);
        }

        public override IEnumerable<PackageVerifierIssue> ValidateAttribute(IPackageMetadata packageMetadata, string currentFilePath, AssemblyDefinition assembly, Collection<CustomAttribute> assemblyAttributes)
        {
            return null;
        }
    }
}
