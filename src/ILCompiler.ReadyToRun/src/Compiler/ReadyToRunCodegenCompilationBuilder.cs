// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using ILCompiler.DependencyAnalysis;
using ILCompiler.DependencyAnalysis.ReadyToRun;
using ILCompiler.DependencyAnalysisFramework;

using Internal.IL;
using Internal.JitInterface;
using Internal.Text;
using Internal.TypeSystem;
using Internal.TypeSystem.Ecma;

using Debug = System.Diagnostics.Debug;

namespace ILCompiler
{
    public sealed class ReadyToRunCodegenCompilationBuilder : CompilationBuilder
    {
        private readonly string _inputFilePath;
        private readonly EcmaModule _inputModule;

        // These need to provide reasonable defaults so that the user can optionally skip
        // calling the Use/Configure methods and still get something reasonable back.
        private KeyValuePair<string, string>[] _ryujitOptions = Array.Empty<KeyValuePair<string, string>>();
        private ILProvider _ilProvider = new ReadyToRunILProvider();

        public ReadyToRunCodegenCompilationBuilder(CompilerTypeSystemContext context, CompilationModuleGroup group, string inputFilePath)
            : base(context, group, new CoreRTNameMangler(new ReadyToRunNodeMangler(), false))
        {
            _inputFilePath = inputFilePath;
            _devirtualizationManager = new DependencyAnalysis.ReadyToRun.DevirtualizationManager(group);

            _inputModule = context.GetModuleFromPath(_inputFilePath);

            // R2R field layout needs compilation group information
            ((ReadyToRunCompilerContext)context).SetCompilationGroup(group);
        }

        public override CompilationBuilder UseBackendOptions(IEnumerable<string> options)
        {
            var builder = new ArrayBuilder<KeyValuePair<string, string>>();

            foreach (string param in options)
            {
                int indexOfEquals = param.IndexOf('=');

                // We're skipping bad parameters without reporting.
                // This is not a mainstream feature that would need to be friendly.
                // Besides, to really validate this, we would also need to check that the config name is known.
                if (indexOfEquals < 1)
                    continue;

                string name = param.Substring(0, indexOfEquals);
                string value = param.Substring(indexOfEquals + 1);

                builder.Add(new KeyValuePair<string, string>(name, value));
            }

            _ryujitOptions = builder.ToArray();

            return this;
        }

        public override CompilationBuilder UseILProvider(ILProvider ilProvider)
        {
            _ilProvider = ilProvider;
            return this;
        }

        protected override ILProvider GetILProvider()
        {
            return _ilProvider;
        }

        public override ILScannerBuilder GetILScannerBuilder(CompilationModuleGroup compilationGroup = null)
        {
            return new ReadyToRunScannerBuilder(_context, compilationGroup ?? _compilationGroup, _nameMangler, GetILProvider());
        }

        public override ICompilation ToCompilation()
        {
            var interopStubManager = new EmptyInteropStubManager();

            ModuleTokenResolver moduleTokenResolver = new ModuleTokenResolver(_compilationGroup, _context);
            SignatureContext signatureContext = new SignatureContext(_inputModule, moduleTokenResolver);

            ReadyToRunCodegenNodeFactory factory = new ReadyToRunCodegenNodeFactory(
                _context,
                _compilationGroup,
                _metadataManager,
                interopStubManager,
                _nameMangler,
                _vtableSliceProvider,
                _dictionaryLayoutProvider,
                moduleTokenResolver,
                signatureContext);

            DependencyAnalyzerBase<NodeFactory> graph = CreateDependencyGraph(factory);

            List<CorJitFlag> corJitFlags = new List<CorJitFlag> { CorJitFlag.CORJIT_FLAG_DEBUG_INFO };

            switch (_optimizationMode)
            {
                case OptimizationMode.None:
                    corJitFlags.Add(CorJitFlag.CORJIT_FLAG_DEBUG_CODE);
                    break;

                case OptimizationMode.PreferSize:
                    corJitFlags.Add(CorJitFlag.CORJIT_FLAG_SIZE_OPT);
                    break;

                case OptimizationMode.PreferSpeed:
                    corJitFlags.Add(CorJitFlag.CORJIT_FLAG_SPEED_OPT);
                    break;

                default:
                    // Not setting a flag results in BLENDED_CODE.
                    break;
            }

            var jitConfig = new JitConfigProvider(corJitFlags, _ryujitOptions);

            return new ReadyToRunCodegenCompilation(
                graph,
                factory,
                _compilationRoots,
                _ilProvider,
                _debugInformationProvider,
                _logger,
                _devirtualizationManager,
                jitConfig,
                _inputFilePath);
        }

        internal class ReadyToRunScannerBuilder : ILScannerBuilder
        {
            internal ReadyToRunScannerBuilder(CompilerTypeSystemContext context, CompilationModuleGroup compilationGroup, NameMangler mangler, ILProvider ilProvider)
                : base(context, compilationGroup, mangler, ilProvider)
            {
            }

            public override IILScanner ToILScanner()
            {
                var nodeFactory = new R2RILScanNodeFactory(_context, _compilationGroup, _metadataManager, _interopStubManager, _nameMangler);
                DependencyAnalyzerBase<NodeFactory> graph = _dependencyTrackingLevel.CreateDependencyGraph(nodeFactory);

                return new ILScanner(graph, nodeFactory, _compilationRoots, _ilProvider, new NullDebugInformationProvider(), _logger);
            }
        }

        /// <summary>
        /// Node factory to be used during IL scanning.
        /// </summary>
        public sealed class R2RILScanNodeFactory : NodeFactory
        {
            public R2RILScanNodeFactory(CompilerTypeSystemContext context, CompilationModuleGroup compilationModuleGroup, MetadataManager metadataManager, InteropStubManager interopStubManager, NameMangler nameMangler)
                : base(context, compilationModuleGroup, metadataManager, interopStubManager, nameMangler, new LazyGenericsDisabledPolicy(), new LazyVTableSliceProvider(), new LazyDictionaryLayoutProvider(), new ExternSymbolsImportedNodeProvider())
            {
            }

            protected override IEETypeNode CreateNecessaryTypeNode(TypeDesc type)
            {
                if (CompilationModuleGroup.ContainsType(type))
                {
                    return new AvailableType(this, type);
                }
                else
                {
                    return new ExternalTypeNode(this, type);
                }
            }

            protected override IEETypeNode CreateConstructedTypeNode(TypeDesc type)
            {
                // Canonical definition types are *not* constructed types (call NecessaryTypeSymbol to get them)
                Debug.Assert(!type.IsCanonicalDefinitionType(CanonicalFormKind.Any));

                if (!type.IsCanonicalSubtype(CanonicalFormKind.Any) && CompilationModuleGroup.ContainsType(type))
                {
                    return new ScannedTypeNode(type);
                }
                else
                {
                    return new ExternalTypeNode(this, type);
                }
            }

            protected override IMethodNode CreateMethodEntrypointNode(MethodDesc method)
            {
                bool isCanon = method.GetCanonMethodTarget(CanonicalFormKind.Specific) == method;
                if (isCanon && CompilationModuleGroup.ContainsMethodBody(method, false))
                {
                    return new ScannedMethodNode(method);
                }
                else
                {
                    return new ExternMethodSymbolNode(this, method);
                }
            }

            protected override IMethodNode CreateUnboxingStubNode(MethodDesc method)
            {
                Debug.Assert(!method.Signature.IsStatic);

                if (method.IsCanonicalMethod(CanonicalFormKind.Specific) && !method.HasInstantiation)
                {
                    // Unboxing stubs to canonical instance methods need a special unboxing stub that unboxes
                    // 'this' and also provides an instantiation argument (we do a calling convention conversion).
                    // We don't do this for generic instance methods though because they don't use the EEType
                    // for the generic context anyway.
                    return new ScannedMethodNode(TypeSystemContext.GetSpecialUnboxingThunk(method, TypeSystemContext.GeneratedAssembly));
                }
                else
                {
                    // Otherwise we just unbox 'this' and don't touch anything else.
                    return new UnboxingStubNode(method, Target);
                }
            }

            protected override ISymbolNode CreateReadyToRunHelperNode(ReadyToRunHelperKey helperCall)
            {
                return new ReadyToRunHelperNode(helperCall.HelperId, helperCall.Target);
            }
        }

        class ScannedTypeNode : DependencyNodeCore<NodeFactory>, IEETypeNode
        {
            private readonly TypeDesc _type;

            public ScannedTypeNode(TypeDesc type)
            {
                _type = type;
            }

            public override bool InterestingForDynamicDependencyAnalysis => false;

            public override bool HasDynamicDependencies => false;

            public override bool HasConditionalStaticDependencies => true;

            public override bool StaticDependenciesAreComputed => true;

            public TypeDesc Type => _type;

            public int Offset => throw new NotImplementedException();

            public bool RepresentsIndirectionCell => false;

            public int ClassCode => throw new NotImplementedException();

            public void AppendMangledName(NameMangler nameMangler, Utf8StringBuilder sb)
            {
                throw new NotImplementedException();
            }

            public int CompareToImpl(ISortableNode other, CompilerComparer comparer)
            {
                throw new NotImplementedException();
            }

            public override IEnumerable<CombinedDependencyListEntry> GetConditionalStaticDependencies(NodeFactory factory)
            {
                if (_type.IsArray)
                    yield break;

                var defType = (DefType)_type;

                foreach (MethodDesc decl in defType.EnumAllVirtualSlots())
                {
                    // Generic virtual methods are tracked by an orthogonal mechanism.
                    if (decl.HasInstantiation)
                        continue;

                    MethodDesc impl = defType.FindVirtualFunctionTargetMethodOnObjectType(decl);
                    if (impl.OwningType == defType && !impl.IsAbstract)
                    {
                        MethodDesc canonImpl = impl.GetCanonMethodTarget(CanonicalFormKind.Specific);
                        yield return new CombinedDependencyListEntry(factory.MethodEntrypoint(canonImpl, _type.IsValueType), factory.VirtualMethodUse(decl), "Virtual method");
                    }
                }

                Debug.Assert(
                    _type == defType ||
                    ((System.Collections.IStructuralEquatable)defType.RuntimeInterfaces).Equals(_type.RuntimeInterfaces,
                    EqualityComparer<DefType>.Default));

                // Add conditional dependencies for interface methods the type implements. For example, if the type T implements
                // interface IFoo which has a method M1, add a dependency on T.M1 dependent on IFoo.M1 being called, since it's
                // possible for any IFoo object to actually be an instance of T.
                foreach (DefType interfaceType in defType.RuntimeInterfaces)
                {
                    Debug.Assert(interfaceType.IsInterface);

                    foreach (MethodDesc interfaceMethod in interfaceType.GetAllMethods())
                    {
                        if (interfaceMethod.Signature.IsStatic)
                            continue;

                        // Generic virtual methods are tracked by an orthogonal mechanism.
                        if (interfaceMethod.HasInstantiation)
                            continue;

                        MethodDesc implMethod = defType.ResolveInterfaceMethodToVirtualMethodOnType(interfaceMethod);
                        if (implMethod != null)
                        {
                            yield return new CombinedDependencyListEntry(factory.VirtualMethodUse(implMethod), factory.VirtualMethodUse(interfaceMethod), "Interface method");
                        }
                    }
                }
            }

            public override IEnumerable<DependencyListEntry> GetStaticDependencies(NodeFactory factory)
            {
                var dependencies = new DependencyList();

                var baseType = _type.BaseType;
                if (baseType != null)
                    dependencies.Add(factory.ConstructedTypeSymbol(baseType.NormalizeInstantiation()), "Base type");

                foreach (var interfaceType in _type.RuntimeInterfaces)
                {
                    dependencies.Add(factory.ConstructedTypeSymbol(interfaceType.NormalizeInstantiation()), "Interface");
                }

                if (!_type.IsArray)
                    dependencies.Add(factory.VTable(_type), "VTable");

                try
                {
                    factory.MetadataManager.GetDependenciesDueToReflectability(ref dependencies, factory, _type);
                }
                catch (TypeSystemException)
                {
                }

                return dependencies;
            }

            public override IEnumerable<CombinedDependencyListEntry> SearchDynamicDependencies(List<DependencyNodeCore<NodeFactory>> markedNodes, int firstNode, NodeFactory factory)
            {
                throw new NotImplementedException();
            }

            protected override string GetName(NodeFactory factory)
            {
                return factory.NameMangler.GetMangledTypeName(_type);
            }
        }
    }
}
