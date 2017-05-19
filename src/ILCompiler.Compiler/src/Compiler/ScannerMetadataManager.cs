// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;

using Internal.TypeSystem;

using ILCompiler.DependencyAnalysis;

using DependencyList = ILCompiler.DependencyAnalysisFramework.DependencyNodeCore<ILCompiler.DependencyAnalysis.NodeFactory>.DependencyList;

namespace ILCompiler
{
    /// <summary>
    /// This class is responsible for managing metadata behaviors specific to IL scanning.
    /// It's expected to be used in connection with the <see cref="ILScanner"/> class, but only when
    /// "everything that got compiled gets metadata, unless reflection blocked" is the metadata policy of choice.
    /// </summary>
    internal class ScannerMetadataManager : MetadataManager
    {
        public ScannerMetadataManager(CompilationModuleGroup compilationModuleGroup, CompilerTypeSystemContext typeSystemContext)
            : base(compilationModuleGroup, typeSystemContext, new BlockedInternalsBlockingPolicy())
        {
        }

        public override MethodDesc GetCanonicalReflectionInvokeStub(MethodDesc method)
        {
            // We don't allocate reflection invoke stubs in the scanning phase for now.
            // We'll eventually need to do that, but when we do it, we need to make sure we're handing out
            // the same invoke stubs that the real metadata manager will hand out.
            // To do that, we'll need to come up with a scheme where we can share the invoke stub caches
            // between metadata managers.
            throw new NotSupportedException();
        }

        public override bool HasReflectionInvokeStubForInvokableMethod(MethodDesc method)
        {
            // See comment in GetCanonicalReflectionInvokeStub above.
            return false;
        }

        public override IEnumerable<ModuleDesc> GetCompilationModulesWithMetadata()
        {
            throw new NotImplementedException();
        }
        
        public override bool WillUseMetadataTokenToReferenceField(FieldDesc field)
        {
            // Answer to this question is irrelevant, but returning true lets us avoid
            // allocating some useless native layout nodes.
            return true;
        }

        public override bool WillUseMetadataTokenToReferenceMethod(MethodDesc method)
        {
            // Answer to this question is irrelevant, but returning true lets us avoid
            // allocating some useless native layout nodes.
            return true;
        }

        protected override void ComputeMetadata(NodeFactory factory, out byte[] metadataBlob, out List<MetadataMapping<MetadataType>> typeMappings, out List<MetadataMapping<MethodDesc>> methodMappings, out List<MetadataMapping<FieldDesc>> fieldMappings)
        {
            // We could make this return empty lists, but it's not expected for anyone to call this because
            // the places that need to call this are only reachable during final object emission and we don't have
            // a final object emission phase in the IL scanner.
            throw new NotSupportedException();
        }

        protected override MetadataCategory GetMetadataCategory(MethodDesc method)
        {
            MetadataCategory result = MetadataCategory.RuntimeMapping;

            if (_compilationModuleGroup.ContainsMethod(method.GetTypicalMethodDefinition()))
                result |= MetadataCategory.Description;

            return result;
        }

        protected override MetadataCategory GetMetadataCategory(TypeDesc type)
        {
            MetadataCategory result = MetadataCategory.RuntimeMapping;

            if (_compilationModuleGroup.ContainsType(type.GetTypeDefinition()))
                result |= MetadataCategory.Description;

            return result;
        }

        protected override MetadataCategory GetMetadataCategory(FieldDesc field)
        {
            MetadataCategory result = MetadataCategory.RuntimeMapping;

            if (_compilationModuleGroup.ContainsType(field.OwningType.GetTypeDefinition()))
                result |= MetadataCategory.Description;

            return result;
        }

        protected override void GetMetadataDependenciesDueToReflectability(ref DependencyList dependencies, NodeFactory factory, MethodDesc method)
        {
            dependencies = dependencies ?? new DependencyList();

            ILScanNodeFactory ilScanFactory = (ILScanNodeFactory)factory;

            dependencies.Add(ilScanFactory.MethodMetadata(method.GetTypicalMethodDefinition()), "Metadata");
        }

        protected override void GetMetadataDependenciesDueToReflectability(ref DependencyList dependencies, NodeFactory factory, TypeDesc type)
        {
            ILScanNodeFactory ilScanFactory = (ILScanNodeFactory)factory;

            TypeMetadataNode.GetMetadataDependencies(ref dependencies, ilScanFactory, type, "Metadata");
        }
    }
}
