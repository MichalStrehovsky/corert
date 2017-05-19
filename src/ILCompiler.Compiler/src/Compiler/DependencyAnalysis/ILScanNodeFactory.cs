// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;

using ILCompiler.DependencyAnalysisFramework;
using Internal.TypeSystem;

using Debug = System.Diagnostics.Debug;

namespace ILCompiler.DependencyAnalysis
{
    /// <summary>
    /// Node factory to be used during IL scanning.
    /// </summary>
    internal sealed class ILScanNodeFactory : NodeFactory
    {
        private readonly ILScanningOptions _options;

        public ILScanNodeFactory(CompilerTypeSystemContext context, CompilationModuleGroup compilationModuleGroup, MetadataManager metadataManager, NameMangler nameMangler, ILScanningOptions options)
            : base(context, compilationModuleGroup, metadataManager, nameMangler, new LazyGenericsDisabledPolicy())
        {
            _options = options;

            _typesWithMetadata = new NodeCache<MetadataType, TypeMetadataNode>(type =>
            {
                return new TypeMetadataNode(type);
            });

            _methodsWithMetadata = new NodeCache<MethodDesc, MethodMetadataNode>(method =>
            {
                return new MethodMetadataNode(method);
            });

            _modulesWithMetadata = new NodeCache<ModuleDesc, ModuleMetadataNode>(module =>
            {
                return new ModuleMetadataNode(module);
            });
        }

        public bool IsScanningForReflectionRoots
        {
            get
            {
                return (_options & ILScanningOptions.ScanForReflectionRoots) != 0;
            }
        }

        protected override IMethodNode CreateMethodEntrypointNode(MethodDesc method)
        {
            if (method.IsInternalCall)
            {
                // TODO: come up with a scheme where this can be shared between codegen backends and the scanner
                if (TypeSystemContext.IsSpecialUnboxingThunkTargetMethod(method))
                {
                    return MethodEntrypoint(TypeSystemContext.GetRealSpecialUnboxingThunkTargetMethod(method));
                }
                else if (method.IsArrayAddressMethod())
                {
                    return new ScannedMethodNode(((ArrayType)method.OwningType).GetArrayMethod(ArrayMethodKind.AddressWithHiddenArg));
                }
                else if (method.HasCustomAttribute("System.Runtime", "RuntimeImportAttribute"))
                {
                    return new RuntimeImportMethodNode(method);
                }

                // On CLR this would throw a SecurityException with "ECall methods must be packaged into a system module."
                // This is a corner case that nobody is likely to care about.
                throw new TypeSystemException.InvalidProgramException(ExceptionStringID.InvalidProgramSpecific, method);
            }

            if (CompilationModuleGroup.ContainsMethod(method))
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
                return new ScannedMethodNode(TypeSystemContext.GetSpecialUnboxingThunk(method, CompilationModuleGroup.GeneratedAssembly));
            }
            else
            {
                // Otherwise we just unbox 'this' and don't touch anything else.
                return new UnboxingStubNode(method);
            }
        }

        protected override ISymbolNode CreateReadyToRunHelperNode(ReadyToRunHelperKey helperCall)
        {
            return new ReadyToRunHelperNode(this, helperCall.HelperId, helperCall.Target);
        }

        /// <summary>
        /// Gets a node that ensures the method will be tracked as compiled and reflectable.
        /// </summary>
        public IDependencyNode<NodeFactory> ScannedMethod(MethodDesc method)
        {
            Debug.Assert(IsScanningForReflectionRoots);
            Debug.Assert(!MetadataManager.IsReflectionBlocked(method));

            // Methods that don't have a body generated need to be tracked through a special mechanism.
            if (ReflectableMethodNode.ShouldTrackMethod(method))
            {
                return ReflectableMethod(method);
            }

            return CanonicalEntrypoint(method);
        }

        private NodeCache<MetadataType, TypeMetadataNode> _typesWithMetadata;

        public TypeMetadataNode TypeMetadata(MetadataType type)
        {
            return _typesWithMetadata.GetOrAdd(type);
        }

        private NodeCache<MethodDesc, MethodMetadataNode> _methodsWithMetadata;

        public MethodMetadataNode MethodMetadata(MethodDesc method)
        {
            return _methodsWithMetadata.GetOrAdd(method);
        }

        private NodeCache<ModuleDesc, ModuleMetadataNode> _modulesWithMetadata;

        public ModuleMetadataNode ModuleMetadata(ModuleDesc module)
        {
            return _modulesWithMetadata.GetOrAdd(module);
        }
    }

    [Flags]
    internal enum ILScanningOptions
    {
        None = 0,
        ScanForReflectionRoots = 0x1,
    }
}
