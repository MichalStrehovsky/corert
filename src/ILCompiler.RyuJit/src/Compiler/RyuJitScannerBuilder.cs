// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using ILCompiler.DependencyAnalysis;
using ILCompiler.DependencyAnalysisFramework;

using Internal.IL;

namespace ILCompiler
{
    internal class RyuJitScannerBuilder : ILScannerBuilder
    {
        internal RyuJitScannerBuilder(CompilerTypeSystemContext context, CompilationModuleGroup compilationGroup, NameMangler mangler, ILProvider ilProvider)
            : base(context, compilationGroup, mangler, ilProvider)
        {
        }

        public override IILScanner ToILScanner()
        {
            var nodeFactory = new ILScanNodeFactory(_context, _compilationGroup, _metadataManager, _interopStubManager, _nameMangler);
            DependencyAnalyzerBase<NodeFactory> graph = _dependencyTrackingLevel.CreateDependencyGraph(nodeFactory);

            return new ILScanner(graph, nodeFactory, _compilationRoots, _ilProvider, new NullDebugInformationProvider(), _logger);
        }
    }
}
