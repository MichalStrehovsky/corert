// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;

using ILCompiler.DependencyAnalysis;
using ILCompiler.DependencyAnalysisFramework;

using Internal.IL;
using Internal.IL.Stubs;
using Internal.TypeSystem;

using Debug = System.Diagnostics.Debug;

namespace ILCompiler
{
    /// <summary>
    /// IL scan analyzer of programs - this class analyzes what methods, types and other runtime artifact
    /// will need to be generated during a compilation. The result of analysis is a conservative superset of
    /// what methods will be compiled by the actual codegen backend.
    /// </summary>
    internal sealed class ILScanner : Compilation, IILScanner
    {
        internal ILScanner(
            DependencyAnalyzerBase<NodeFactory> dependencyGraph,
            ILScanNodeFactory nodeFactory,
            IEnumerable<ICompilationRootProvider> roots,
            ILProvider ilProvider,
            DebugInformationProvider debugInformationProvider,
            PInvokeILEmitterConfiguration pinvokePolicy,
            Logger logger)
            : base(dependencyGraph, nodeFactory, null, roots, ilProvider, debugInformationProvider, null, pinvokePolicy, logger)
        {
        }

        protected override void CompileInternal(string outputFile, ObjectDumper dumper)
        {
            // TODO: We should have a base class for compilation that doesn't implement ICompilation so that
            // we don't need this.
            throw new NotSupportedException();
        }

        protected override void ComputeDependencyNodeDependencies(List<DependencyNodeCore<NodeFactory>> obj)
        {
            foreach (DependencyNodeCore<NodeFactory> dependency in obj)
            {
                var methodCodeNodeNeedingCode = dependency as ScannedMethodNode;
                if (methodCodeNodeNeedingCode == null)
                {
                    // To compute dependencies of the shadow method that tracks dictionary
                    // dependencies we need to ensure there is code for the canonical method body.
                    var dependencyMethod = (ShadowConcreteMethodNode)dependency;
                    methodCodeNodeNeedingCode = (ScannedMethodNode)dependencyMethod.CanonicalMethodNode;
                }

                // We might have already compiled this method.
                if (methodCodeNodeNeedingCode.StaticDependenciesAreComputed)
                    continue;

                MethodDesc method = methodCodeNodeNeedingCode.Method;

                try
                {
                    var importer = new ILImporter(this, method);
                    methodCodeNodeNeedingCode.InitializeDependencies(_nodeFactory, importer.Import());
                }
                catch (TypeSystemException ex)
                {
                    // Try to compile the method again, but with a throwing method body this time.
                    MethodIL throwingIL = TypeSystemThrowingILEmitter.EmitIL(method, ex);
                    var importer = new ILImporter(this, method, throwingIL);
                    methodCodeNodeNeedingCode.InitializeDependencies(_nodeFactory, importer.Import());
                }
            }
        }

        ILScanResults IILScanner.Scan()
        {
            _dependencyGraph.ComputeMarkedNodes();

            return new ILScanResults(_dependencyGraph, _nodeFactory);
        }
    }

    public interface IILScanner
    {
        ILScanResults Scan();
    }

    internal class ScannerFailedException : InternalCompilerErrorException
    {
        public ScannerFailedException(string message)
            : base(message + " " + "You can work around by running the compilation with scanner disabled.")
        {
        }
    }

    public class ILScanResults : CompilationResults
    {
        internal ILScanResults(DependencyAnalyzerBase<NodeFactory> graph, NodeFactory factory)
            : base(graph, factory)
        {
        }

        public VTableSliceProvider GetVTableLayoutInfo()
        {
            return new ScannedVTableProvider(MarkedNodes);
        }

        public DictionaryLayoutProvider GetDictionaryLayoutInfo()
        {
            return new ScannedDictionaryLayoutProvider(MarkedNodes);
        }

        public DevirtualizationManager GetDevirtualizationManager()
        {
            return new ScannedDevirtualizationManager(MarkedNodes);
        }

        public InliningPolicy GetInliningPolicy()
        {
            return new ScannedInliningPolicy(MarkedNodes);
        }

        private class ScannedVTableProvider : VTableSliceProvider
        {
            private Dictionary<TypeDesc, IReadOnlyList<MethodDesc>> _vtableSlices = new Dictionary<TypeDesc, IReadOnlyList<MethodDesc>>();

            public ScannedVTableProvider(ImmutableArray<DependencyNodeCore<NodeFactory>> markedNodes)
            {
                foreach (var node in markedNodes)
                {
                    var vtableSliceNode = node as VTableSliceNode;
                    if (vtableSliceNode != null)
                    {
                        _vtableSlices.Add(vtableSliceNode.Type, vtableSliceNode.Slots);
                    }
                }
            }

            internal override VTableSliceNode GetSlice(TypeDesc type)
            {
                // TODO: move ownership of compiler-generated entities to CompilerTypeSystemContext.
                // https://github.com/dotnet/corert/issues/3873
                if (type.GetTypeDefinition() is Internal.TypeSystem.Ecma.EcmaType)
                {
                    if (!_vtableSlices.TryGetValue(type, out IReadOnlyList<MethodDesc> slots))
                    {
                        // If we couln't find the vtable slice information for this type, it's because the scanner
                        // didn't correctly predict what will be needed.
                        // To troubleshoot, compare the dependency graph of the scanner and the compiler.
                        // Follow the path from the node that requested this node to the root.
                        // On the path, you'll find a node that exists in both graphs, but it's predecessor
                        // only exists in the compiler's graph. That's the place to focus the investigation on.
                        // Use the ILCompiler-DependencyGraph-Viewer tool to investigate.
                        Debug.Assert(false);
                        string typeName = ExceptionTypeNameFormatter.Instance.FormatName(type);
                        throw new ScannerFailedException($"VTable of type '{typeName}' not computed by the IL scanner.");
                    }
                    return new PrecomputedVTableSliceNode(type, slots);
                }
                else
                    return new LazilyBuiltVTableSliceNode(type);
            }
        }

        private class ScannedDictionaryLayoutProvider : DictionaryLayoutProvider
        {
            private Dictionary<TypeSystemEntity, IEnumerable<GenericLookupResult>> _layouts = new Dictionary<TypeSystemEntity, IEnumerable<GenericLookupResult>>();

            public ScannedDictionaryLayoutProvider(ImmutableArray<DependencyNodeCore<NodeFactory>> markedNodes)
            {
                foreach (var node in markedNodes)
                {
                    var layoutNode = node as DictionaryLayoutNode;
                    if (layoutNode != null)
                    {
                        TypeSystemEntity owningMethodOrType = layoutNode.OwningMethodOrType;
                        _layouts.Add(owningMethodOrType, layoutNode.Entries);
                    }
                }
            }

            private DictionaryLayoutNode GetPrecomputedLayout(TypeSystemEntity methodOrType)
            {
                if (!_layouts.TryGetValue(methodOrType, out IEnumerable<GenericLookupResult> layout))
                {
                    // If we couln't find the dictionary layout information for this, it's because the scanner
                    // didn't correctly predict what will be needed.
                    // To troubleshoot, compare the dependency graph of the scanner and the compiler.
                    // Follow the path from the node that requested this node to the root.
                    // On the path, you'll find a node that exists in both graphs, but it's predecessor
                    // only exists in the compiler's graph. That's the place to focus the investigation on.
                    // Use the ILCompiler-DependencyGraph-Viewer tool to investigate.
                    Debug.Assert(false);
                    throw new ScannerFailedException($"A dictionary layout was not computed by the IL scanner.");
                }
                return new PrecomputedDictionaryLayoutNode(methodOrType, layout);
            }

            public override DictionaryLayoutNode GetLayout(TypeSystemEntity methodOrType)
            {
                if (methodOrType is TypeDesc type)
                {
                    // TODO: move ownership of compiler-generated entities to CompilerTypeSystemContext.
                    // https://github.com/dotnet/corert/issues/3873
                    if (type.GetTypeDefinition() is Internal.TypeSystem.Ecma.EcmaType)
                        return GetPrecomputedLayout(type);
                    else
                        return new LazilyBuiltDictionaryLayoutNode(type);
                }
                else
                {
                    Debug.Assert(methodOrType is MethodDesc);
                    MethodDesc method = (MethodDesc)methodOrType;

                    // TODO: move ownership of compiler-generated entities to CompilerTypeSystemContext.
                    // https://github.com/dotnet/corert/issues/3873
                    if (method.GetTypicalMethodDefinition() is Internal.TypeSystem.Ecma.EcmaMethod)
                        return GetPrecomputedLayout(method);
                    else
                        return new LazilyBuiltDictionaryLayoutNode(method);
                }
            }
        }

        private class ScannedDevirtualizationManager : DevirtualizationManager
        {
            private HashSet<TypeDesc> _unsealedTypes = new HashSet<TypeDesc>();

            public ScannedDevirtualizationManager(ImmutableArray<DependencyNodeCore<NodeFactory>> markedNodes)
            {
                foreach (var node in markedNodes)
                {
                    if (node is ConstructedEETypeNode eetypeNode)
                    {
                        TypeDesc type = eetypeNode.Type;

                        if (!type.IsInterface)
                        {
                            //
                            // We collect this information about what types are the base types of other types
                            // This is needed for optimizations. We use this information to effectively
                            // seal types that are not base types for any other type.
                            //

                            TypeDesc canonType = type.ConvertToCanonForm(CanonicalFormKind.Specific);

                            TypeDesc baseType = canonType.BaseType;
                            bool added = true;
                            while (baseType != null && added)
                            {
                                baseType = baseType.ConvertToCanonForm(CanonicalFormKind.Specific);
                                added = _unsealedTypes.Add(baseType);
                                baseType = baseType.BaseType;
                            }
                        }

                    }
                }
            }

            public override bool IsEffectivelySealed(TypeDesc type)
            {
                // If we know we scanned a type that derives from this one, this for sure can't be reported as sealed.
                TypeDesc canonType = type.ConvertToCanonForm(CanonicalFormKind.Specific);
                if (_unsealedTypes.Contains(canonType))
                    return false;

                if (type is MetadataType metadataType)
                {
                    // Due to how the compiler is structured, we might see "constructed" EETypes for things
                    // that never got allocated (doing a typeof() on a class that is otherwise never used is
                    // a good example of when that happens). This can put us into a position where we could
                    // report `sealed` on an `abstract` class, but that doesn't lead to anything good.
                    return !metadataType.IsAbstract;
                }

                // Everything else can be considered sealed.
                return true;
            }
        }

        private class ScannedInliningPolicy : InliningPolicy
        {
            private readonly HashSet<TypeDesc> _constructedTypes = new HashSet<TypeDesc>();

            public ScannedInliningPolicy(ImmutableArray<DependencyNodeCore<NodeFactory>> markedNodes)
            {
                foreach (var node in markedNodes)
                {
                    TypeDesc type = null;
                    if (node is ConstructedEETypeNode eetypeNode)
                        type = eetypeNode.Type;
                    else if (node is CanonicalEETypeNode canonEETypeNode)
                        type = canonEETypeNode.Type;

                    if (type != null)
                    {
                        _constructedTypes.Add(type);

                        // Since this is used for the purposes of inlining that might happen as a result
                        // of devirtualization, it's really convenient to also have Array<T> for each T[].
                        if (type.IsArray)
                            _constructedTypes.Add(type.GetClosestDefType());
                    }
                }
            }

            public override bool CanInline(MethodDesc callerMethod, MethodDesc calleeMethod)
            {
                if (calleeMethod.OwningType.ToString().Contains("Fun`"))
                    System.Diagnostics.Debugger.Break();

                // We want to limit inlining of instance methods
                if (!calleeMethod.Signature.IsStatic && !calleeMethod.OwningType.IsValueType)
                {
                    if (_constructedTypes.Contains(calleeMethod.OwningType))
                        return true;
                    else
                        return false;
                }

                return true;
            }
        }
    }
}
