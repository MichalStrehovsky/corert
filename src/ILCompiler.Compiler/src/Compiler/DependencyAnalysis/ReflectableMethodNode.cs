﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;

using ILCompiler.DependencyAnalysisFramework;

using Internal.TypeSystem;

using Debug = System.Diagnostics.Debug;

namespace ILCompiler.DependencyAnalysis
{
    /// <summary>
    /// Represents a method that doesn't have a body, but we need to track its dependencies as if it was a body
    /// because it's reflectable.
    /// </summary>
    internal class ReflectableMethodNode : DependencyNodeCore<NodeFactory>
    {
        private readonly MethodDesc _method;

        public ReflectableMethodNode(MethodDesc method)
        {
            Debug.Assert(ShouldTrackMethod(method));
            _method = method;
        }

        public MethodDesc Method => _method;

        /// <summary>
        /// Returns true if '<paramref name="method"/>' should be tracked as a <see cref="ReflectableMethodNode"/>
        /// as opposed to a regular entrypoint.
        /// </summary>
        public static bool ShouldTrackMethod(MethodDesc method)
        {
            if (method.IsAbstract || method.IsRawPInvoke())
            {
                // These don't have a body
                return true;
            }

            if (method.IsConstructor && method.OwningType.IsString)
            {
                // String constructors don't actually exist
                return true;
            }

            return false;
        }

        public override IEnumerable<DependencyListEntry> GetStaticDependencies(NodeFactory factory)
        {
            DependencyList dependencies = null;
            factory.MetadataManager.GetDependenciesDueToReflectability(ref dependencies, factory, _method);
            return dependencies;
        }
        protected override string GetName(NodeFactory factory)
        {
            return "Reflectable method: " + _method.ToString();
        }

        public override bool InterestingForDynamicDependencyAnalysis => false;
        public override bool HasDynamicDependencies => false;
        public override bool HasConditionalStaticDependencies => false;
        public override bool StaticDependenciesAreComputed => true;
        public override IEnumerable<CombinedDependencyListEntry> GetConditionalStaticDependencies(NodeFactory factory) => null;
        public override IEnumerable<CombinedDependencyListEntry> SearchDynamicDependencies(List<DependencyNodeCore<NodeFactory>> markedNodes, int firstNode, NodeFactory factory) => null;
    }
}