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
    /// Represents a reflectable module.
    /// </summary>
    /// <remarks>
    /// Only expected to be used during ILScanning when scanning for reflection.
    /// </remarks>
    internal class ModuleMetadataNode : DependencyNodeCore<NodeFactory>
    {
        private readonly ModuleDesc _module;

        public ModuleMetadataNode(ModuleDesc module)
        {
            Debug.Assert(module is IAssemblyDesc, "Multi-module assemblies?");
            _module = module;
        }

        public override IEnumerable<DependencyListEntry> GetStaticDependencies(NodeFactory factory)
        {
            Debug.Assert(factory is ILScanNodeFactory);

            DependencyList dependencies = null;
            CustomAttributeBasedDependencyAlgorithm.AddDependenciesDueToCustomAttributes(ref dependencies, (ILScanNodeFactory)factory,
                (Internal.TypeSystem.Ecma.EcmaAssembly)_module);
            return dependencies;
        }

        protected override string GetName(NodeFactory factory)
        {
            return "Reflectable module: " + ((IAssemblyDesc)_module).GetName().FullName;
        }

        public override bool InterestingForDynamicDependencyAnalysis => false;
        public override bool HasDynamicDependencies => false;
        public override bool HasConditionalStaticDependencies => false;
        public override bool StaticDependenciesAreComputed => true;
        public override IEnumerable<CombinedDependencyListEntry> GetConditionalStaticDependencies(NodeFactory factory) => null;
        public override IEnumerable<CombinedDependencyListEntry> SearchDynamicDependencies(List<DependencyNodeCore<NodeFactory>> markedNodes, int firstNode, NodeFactory factory) => null;
    }
}
