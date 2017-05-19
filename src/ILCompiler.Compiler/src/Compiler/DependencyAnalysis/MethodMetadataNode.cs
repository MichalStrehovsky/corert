// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;

using ILCompiler.DependencyAnalysisFramework;

using Internal.TypeSystem;

using Debug = System.Diagnostics.Debug;

namespace ILCompiler.DependencyAnalysis
{
    /// <summary>
    /// Represents a method that has metadata generated in the current compilation.
    /// </summary>
    /// <remarks>
    /// Only expected to be used during ILScanning when scanning for reflection.
    /// </remarks>
    class MethodMetadataNode : DependencyNodeCore<NodeFactory>
    {
        private readonly MethodDesc _method;

        public MethodMetadataNode(MethodDesc method)
        {
            Debug.Assert(method.IsTypicalMethodDefinition);
            _method = method;
        }

        public MethodDesc Method => _method;

        public override IEnumerable<DependencyListEntry> GetStaticDependencies(NodeFactory factory)
        {
            Debug.Assert(factory is ILScanNodeFactory);
            var ilScanNodeFactory = (ILScanNodeFactory)factory;

            DependencyList dependencies = new DependencyList();
            dependencies.Add(ilScanNodeFactory.TypeMetadata((MetadataType)_method.OwningType), "Owning type metadata");

            CustomAttributeBasedDependencyAlgorithm.AddDependenciesDueToCustomAttributes(ref dependencies, ilScanNodeFactory, ((Internal.TypeSystem.Ecma.EcmaMethod)_method));

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
