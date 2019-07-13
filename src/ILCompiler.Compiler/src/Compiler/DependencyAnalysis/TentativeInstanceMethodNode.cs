// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;

using ILCompiler.DependencyAnalysisFramework;

using Internal.IL;
using Internal.Text;
using Internal.TypeSystem;

using Debug = System.Diagnostics.Debug;

namespace ILCompiler.DependencyAnalysis
{
    partial class TentativeInstanceMethodNode : AssemblyStubNode, IMethodNode, ISymbolNodeWithLinkage
    {
        private readonly IMethodBodyNode _methodNode;

        public IMethodBodyNode RealBody => _methodNode;

        public TentativeInstanceMethodNode(IMethodBodyNode methodNode)
        {
            Debug.Assert(!methodNode.Method.Signature.IsStatic);
            Debug.Assert(!methodNode.Method.OwningType.IsValueType);
            _methodNode = methodNode;
        }

        private ISymbolNode GetTarget(NodeFactory factory)
        {
            // TODO: Make a new entrypoint
            MethodDesc helper = factory.TypeSystemContext.GetHelperEntryPoint("ThrowHelpers", "ThrowOverflowException");
            return factory.MethodEntrypoint(helper);
        }

        public MethodDesc Method => _methodNode.Method;

        public override IEnumerable<CombinedDependencyListEntry> GetConditionalStaticDependencies(NodeFactory context)
        {
            return new CombinedDependencyListEntry[]
            {
                new CombinedDependencyListEntry(
                    _methodNode,
                    context.ConstructedTypeSymbol(_methodNode.Method.OwningType),
                    "Instance method on a constructed type"),
            };
        }

        protected override string GetName(NodeFactory context)
        {
            return "Tentative instance method: " + _methodNode.GetMangledName(context.NameMangler);
        }

        public override bool ShouldSkipEmittingObjectNode(NodeFactory factory)
        {
            return _methodNode.Marked;
        }

        public override IEnumerable<CombinedDependencyListEntry> SearchDynamicDependencies(List<DependencyNodeCore<NodeFactory>> markedNodes, int firstNode, NodeFactory context) => null;
        public override bool InterestingForDynamicDependencyAnalysis => false;
        public override bool HasDynamicDependencies => false;
        public override bool HasConditionalStaticDependencies => true;

        public override void AppendMangledName(NameMangler nameMangler, Utf8StringBuilder sb)
        {
            _methodNode.AppendMangledName(nameMangler, sb);
        }

        public override int CompareToImpl(ISortableNode other, CompilerComparer comparer)
        {
            return _methodNode.CompareToImpl(((TentativeInstanceMethodNode)other)._methodNode, comparer);
        }

        public ISymbolNode NodeForLinkage(NodeFactory factory)
        {
            return _methodNode.Marked ? _methodNode : (ISymbolNode)this;
        }

        public override bool RepresentsIndirectionCell
        {
            get
            {
                Debug.Assert(!_methodNode.RepresentsIndirectionCell);
                return false;
            }
        }

        public override int ClassCode => 0x562912;

        public override bool IsShareable => ((ObjectNode)_methodNode).IsShareable;
    }
}
