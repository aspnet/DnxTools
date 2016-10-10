﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ApiCheck.Baseline;
using ApiCheckBaseline.V2;
using Scenarios;
using Xunit;

namespace ApiCheck.Test
{
    public class BaselineComparerTests
    {
        public Assembly V1Assembly => typeof(ApiCheckBaselineV1).GetTypeInfo().Assembly;
        public Assembly V2Assembly => typeof(ApiCheckBaselineV2).GetTypeInfo().Assembly;

        public IEnumerable<Func<TypeInfo, bool>> TypeFilters => new Func<TypeInfo, bool>[]{
            ti => !ti.Namespace.Equals("Scenarios")
        };


        [Fact]
        public void Compare_Detects_ChangesInTypeVisibility()
        {
            // Arrange
            var v1Baseline = CreateBaselineDocument(V1Assembly);
            var v2Baseline = CreateBaselineDocument(V2Assembly);
            var exceptions = CreateDefault();

            var comparer = new BaselineComparer(v1Baseline, v2Baseline, BreakingChangeTypes.All, exceptions);

            // Act
            var changes = comparer.GetDifferences();

            // Assert
            var change = Assert.Single(changes, bc => bc.OldItem.Id == "public class ComparisonScenarios.PublicToInternalClass");
        }

        [Fact]
        public void Compare_Detects_TypeRenames()
        {
            // Arrange
            var v1Baseline = CreateBaselineDocument(V1Assembly);
            var v2Baseline = CreateBaselineDocument(V2Assembly);
            var exceptions = CreateDefault();

            var comparer = new BaselineComparer(v1Baseline, v2Baseline, BreakingChangeTypes.All, exceptions);

            // Act
            var changes = comparer.GetDifferences();

            // Assert
            var change = Assert.Single(changes, bc => bc.OldItem.Id == "public interface ComparisonScenarios.TypeToRename");
        }

        [Fact]
        public void Compare_Detects_TypeGenericityChanges()
        {
            // Arrange
            var v1Baseline = CreateBaselineDocument(V1Assembly);
            var v2Baseline = CreateBaselineDocument(V2Assembly);
            var exceptions = CreateDefault();

            var comparer = new BaselineComparer(v1Baseline, v2Baseline, BreakingChangeTypes.All, exceptions);

            // Act
            var changes = comparer.GetDifferences();

            // Assert
            var change = Assert.Single(changes, bc => bc.OldItem.Id == "public struct ComparisonScenarios.StructToMakeGeneric");
        }

        [Fact]
        public void Compare_Detects_NamespaceChanges()
        {
            // Arrange
            var v1Baseline = CreateBaselineDocument(V1Assembly);
            var v2Baseline = CreateBaselineDocument(V2Assembly);
            var exceptions = CreateDefault();

            var comparer = new BaselineComparer(v1Baseline, v2Baseline, BreakingChangeTypes.All, exceptions);

            // Act
            var changes = comparer.GetDifferences();

            // Assert
            var change = Assert.Single(changes, bc => bc.OldItem.Id == "public class ComparisonScenarios.ClassToChangeNamespaces");
        }

        [Fact]
        public void Compare_Detects_ClassBeingNested()
        {
            // Arrange
            var v1Baseline = CreateBaselineDocument(V1Assembly);
            var v2Baseline = CreateBaselineDocument(V2Assembly);
            var exceptions = CreateDefault();

            var comparer = new BaselineComparer(v1Baseline, v2Baseline, BreakingChangeTypes.All, exceptions);

            // Act
            var changes = comparer.GetDifferences();

            // Assert
            var change = Assert.Single(changes, bc => bc.OldItem.Id == "public class ComparisonScenarios.ClassToNest");
        }

        [Fact]
        public void Compare_Detects_ClassBeingUnnested()
        {
            // Arrange
            var v1Baseline = CreateBaselineDocument(V1Assembly);
            var v2Baseline = CreateBaselineDocument(V2Assembly);
            var exceptions = CreateDefault();

            var comparer = new BaselineComparer(v1Baseline, v2Baseline, BreakingChangeTypes.All, exceptions);

            // Act
            var changes = comparer.GetDifferences();

            // Assert
            var change = Assert.Single(changes, bc => bc.OldItem.Id == "public class ComparisonScenarios.ClassToUnnestContainer+ClassToUnnest");
        }

        [Fact]
        public void Compare_Detects_GenericTypeConstraintsBeingAdded()
        {
            // Arrange
            var v1Baseline = CreateBaselineDocument(V1Assembly);
            var v2Baseline = CreateBaselineDocument(V2Assembly);
            var exceptions = CreateDefault();

            var comparer = new BaselineComparer(v1Baseline, v2Baseline, BreakingChangeTypes.All, exceptions);

            // Act
            var changes = comparer.GetDifferences();

            // Assert
            var change = Assert.Single(changes, bc => bc.OldItem.Id == "public class ComparisonScenarios.GenericTypeWithConstraintsToBeAdded<TToConstrain>");
            Assert.Equal("public class ComparisonScenarios.GenericTypeWithConstraintsToBeAdded<TToConstrain> where TToConstrain : System.Collections.Generic.IEnumerable<TToConstrain>, new()", change.NewItem.Id);
        }

        [Fact]
        public void Compare_Detects_MethodParametersBeingAdded()
        {
            // Arrange
            var v1Baseline = CreateBaselineDocument(V1Assembly);
            var v2Baseline = CreateBaselineDocument(V2Assembly);
            var exceptions = CreateDefault();

            var comparer = new BaselineComparer(v1Baseline, v2Baseline, BreakingChangeTypes.All, exceptions);

            // Act
            var changes = comparer.GetDifferences();

            // Assert
            var change = Assert.Single(changes, bc => bc.OldItem.Id == "public System.Void MethodToAddParameters()");
            Assert.Equal("public System.Void MethodToAddParameters(System.Int32 addedParameter)", change.NewItem.Id);
        }

        private static IList<Func<BreakingChangeContext, bool>> CreateDefault(params Func<BreakingChangeContext, bool>[] additionalHandlers)
        {
            return new List<Func<BreakingChangeContext, bool>>() {
                    BreakingChangeHandlers.FindTypeUsingFullName,
                    BreakingChangeHandlers.FindMemberUsingName
                }
            .Concat(additionalHandlers).ToList();
        }

        private BaselineDocument CreateBaselineDocument(Assembly assembly, IEnumerable<Func<TypeInfo, bool>> additionalFilters = null)
        {
            additionalFilters = additionalFilters ?? Enumerable.Empty<Func<TypeInfo, bool>>();
            var generator = new BaselineGenerator(assembly, TypeFilters.Concat(additionalFilters));

            return generator.GenerateBaseline();
        }
    }
}