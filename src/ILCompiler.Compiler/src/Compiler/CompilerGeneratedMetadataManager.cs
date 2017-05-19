// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.IO;
using System.Collections.Generic;
using System.Text;

using Internal.IL.Stubs;
using Internal.TypeSystem;
using Internal.Metadata.NativeFormat.Writer;

using ILCompiler.Metadata;
using ILCompiler.DependencyAnalysis;

using Debug = System.Diagnostics.Debug;

namespace ILCompiler
{
    /// <summary>
    /// This class is responsible for managing native metadata to be emitted into the compiled
    /// module. It applies a policy that every type/method emitted shall be reflectable.
    /// </summary>
    public class CompilerGeneratedMetadataManager : MetadataManager, ICompilationRootProvider
    {
        private readonly GeneratedTypesAndCodeMetadataPolicy _metadataPolicy;
        private readonly string _metadataLogFile;
        private readonly IReflectionRootProvider _reflectionRoots;

        public CompilerGeneratedMetadataManager(CompilationModuleGroup group, CompilerTypeSystemContext typeSystemContext, IReflectionRootProvider reflectionRoots, string logFile)
            : base(group, typeSystemContext, new BlockedInternalsBlockingPolicy())
        {
            _reflectionRoots = reflectionRoots;
            _metadataLogFile = logFile;

            HashSet<TypeDesc> typeDefinitionsToGenerate = new HashSet<TypeDesc>();
            foreach (MetadataType type in reflectionRoots.TypesWithMetadata)
            {
                typeDefinitionsToGenerate.Add(type);
                _modulesSeen.Add(type.Module);
            }

            HashSet<MethodDesc> methodDefinitionsToGenerate = new HashSet<MethodDesc>();
            foreach (MethodDesc method in reflectionRoots.MethodsWithMetadata)
            {
                methodDefinitionsToGenerate.Add(method);
            }

            _metadataPolicy = new GeneratedTypesAndCodeMetadataPolicy(this, typeDefinitionsToGenerate, methodDefinitionsToGenerate);
        }

        private HashSet<ModuleDesc> _modulesSeen = new HashSet<ModuleDesc>();
        private Dictionary<DynamicInvokeMethodSignature, MethodDesc> _dynamicInvokeThunks = new Dictionary<DynamicInvokeMethodSignature, MethodDesc>();

        public override IEnumerable<ModuleDesc> GetCompilationModulesWithMetadata()
        {
            return _modulesSeen;
        }

        public override bool WillUseMetadataTokenToReferenceMethod(MethodDesc method)
        {
            return _compilationModuleGroup.ContainsType(method.GetTypicalMethodDefinition().OwningType);
        }

        public override bool WillUseMetadataTokenToReferenceField(FieldDesc field)
        {
            return _compilationModuleGroup.ContainsType(field.GetTypicalFieldDefinition().OwningType);
        }

        protected override MetadataCategory GetMetadataCategory(FieldDesc field)
        {
            return MetadataCategory.RuntimeMapping;
        }

        protected override MetadataCategory GetMetadataCategory(MethodDesc method)
        {
            return MetadataCategory.RuntimeMapping;
        }

        protected override MetadataCategory GetMetadataCategory(TypeDesc type)
        {
            return MetadataCategory.RuntimeMapping;
        }

        protected override void ComputeMetadata(NodeFactory factory,
                                                out byte[] metadataBlob, 
                                                out List<MetadataMapping<MetadataType>> typeMappings,
                                                out List<MetadataMapping<MethodDesc>> methodMappings,
                                                out List<MetadataMapping<FieldDesc>> fieldMappings)
        {
            var transformed = MetadataTransform.Run(_metadataPolicy, _modulesSeen);

            // TODO: DeveloperExperienceMode: Use transformed.Transform.HandleType() to generate
            //       TypeReference records for _typeDefinitionsGenerated that don't have metadata.
            //       (To be used in MissingMetadataException messages)

            // Generate metadata blob
            var writer = new MetadataWriter();
            writer.ScopeDefinitions.AddRange(transformed.Scopes);
            var ms = new MemoryStream();

            // .NET metadata is UTF-16 and UTF-16 contains code points that don't translate to UTF-8.
            var noThrowUtf8Encoding = new UTF8Encoding(false, false);

            using (var logWriter = _metadataLogFile != null ? new StreamWriter(File.Open(_metadataLogFile, FileMode.Create, FileAccess.Write, FileShare.Read), noThrowUtf8Encoding) : null)
            {
                writer.LogWriter = logWriter;
                writer.Write(ms);
            }

            metadataBlob = ms.ToArray();

            typeMappings = new List<MetadataMapping<MetadataType>>();
            methodMappings = new List<MetadataMapping<MethodDesc>>();
            fieldMappings = new List<MetadataMapping<FieldDesc>>();

            // Generate type definition mappings
            foreach (var type in factory.MetadataManager.GetTypesWithEETypes())
            {
                MetadataType definition = type.IsTypeDefinition ? type as MetadataType : null;
                if (definition == null)
                    continue;

                MetadataRecord record = transformed.GetTransformedTypeDefinition(definition);

                // Reflection requires that we maintain type identity. Even if we only generated a TypeReference record,
                // if there is an EEType for it, we also need a mapping table entry for it.
                if (record == null)
                    record = transformed.GetTransformedTypeReference(definition);

                if (record != null)
                    typeMappings.Add(new MetadataMapping<MetadataType>(definition, writer.GetRecordHandle(record)));
            }

            foreach (var method in _reflectionRoots.InvokableMethods)
            {
                MetadataRecord record = transformed.GetTransformedMethodDefinition(method.GetTypicalMethodDefinition());

                if (record != null)
                    methodMappings.Add(new MetadataMapping<MethodDesc>(method, writer.GetRecordHandle(record)));
            }

            foreach (var eetypeGenerated in GetTypesWithEETypes())
            {
                if (eetypeGenerated.IsGenericDefinition)
                    continue;

                if (eetypeGenerated.HasInstantiation)
                {
                    // Collapsing of field map entries based on canonicalization, to avoid redundant equivalent entries

                    TypeDesc canonicalType = eetypeGenerated.ConvertToCanonForm(CanonicalFormKind.Specific);
                    if (canonicalType != eetypeGenerated && TypeGeneratesEEType(canonicalType))
                        continue;
                }

                foreach (FieldDesc field in eetypeGenerated.GetFields())
                {
                    Field record = transformed.GetTransformedFieldDefinition(field.GetTypicalFieldDefinition());
                    if (record != null)
                        fieldMappings.Add(new MetadataMapping<FieldDesc>(field, writer.GetRecordHandle(record)));
                }
            }
        }

        /// <summary>
        /// Is there a reflection invoke stub for a method that is invokable?
        /// </summary>
        public override bool HasReflectionInvokeStubForInvokableMethod(MethodDesc method)
        {
            Debug.Assert(IsReflectionInvokable(method));
            return true;
        }

        /// <summary>
        /// Gets a stub that can be used to reflection-invoke a method with a given signature.
        /// </summary>
        public override MethodDesc GetCanonicalReflectionInvokeStub(MethodDesc method)
        {
            TypeSystemContext context = method.Context;
            var sig = method.Signature;

            // Get a generic method that can be used to invoke method with this shape.
            MethodDesc thunk;
            var lookupSig = new DynamicInvokeMethodSignature(sig);
            if (!_dynamicInvokeThunks.TryGetValue(lookupSig, out thunk))
            {
                thunk = new DynamicInvokeMethodThunk(_compilationModuleGroup.GeneratedAssembly.GetGlobalModuleType(), lookupSig);
                _dynamicInvokeThunks.Add(lookupSig, thunk);
            }

            return InstantiateCanonicalDynamicInvokeMethodForMethod(thunk, method);
        }

        void ICompilationRootProvider.AddCompilationRoots(IRootingServiceProvider rootProvider)
        {
            foreach (var method in _reflectionRoots.InvokableMethods)
            {
                rootProvider.AddCompilationRoot(method, "Reflection root");
                
            }

            foreach (var type in _reflectionRoots.InvokableTypes)
            {
                rootProvider.AddCompilationRoot(type, "Reflection root");
                Debug.WriteLine(type);
            }
        }

        private struct GeneratedTypesAndCodeMetadataPolicy : IMetadataPolicy
        {
            private CompilerGeneratedMetadataManager _parent;
            private HashSet<TypeDesc> _typeDefinitions;
            private HashSet<MethodDesc> _methodDefinitions;
            private ExplicitScopeAssemblyPolicyMixin _explicitScopeMixin;

            public GeneratedTypesAndCodeMetadataPolicy(CompilerGeneratedMetadataManager parent, HashSet<TypeDesc> typeDefinitions, HashSet<MethodDesc> methodDefinitions)
            {
                _parent = parent;
                _typeDefinitions = typeDefinitions;
                _methodDefinitions = methodDefinitions;
                _explicitScopeMixin = new ExplicitScopeAssemblyPolicyMixin();
            }

            public bool GeneratesMetadata(FieldDesc fieldDef)
            {
                Debug.Assert(fieldDef.OwningType.IsTypeDefinition);
                return _typeDefinitions.Contains(fieldDef.OwningType);
            }

            public bool GeneratesMetadata(MethodDesc methodDef)
            {
                Debug.Assert(methodDef.IsTypicalMethodDefinition);
                return _methodDefinitions.Contains(methodDef);
            }

            public bool GeneratesMetadata(MetadataType typeDef)
            {
                Debug.Assert(typeDef.IsTypeDefinition);

                // Global module type always generates metadata. This is e.g. used in various places
                // where we need a metadata enabled type from an assembly but we don't have a convenient way
                // to find one.
                // We don't need to worry about metadata consistency (accidentally generating metadata
                // that can't be used with any reflection API at runtime because it's incomplete) because
                // global module types don't derive from anything and have an empty interface list.
                if (typeDef.IsModuleType)
                    return true;

                return _typeDefinitions.Contains(typeDef);
            }

            public bool IsBlocked(MetadataType typeDef)
            {
                return _parent.IsReflectionBlocked(typeDef);
            }

            public ModuleDesc GetModuleOfType(MetadataType typeDef)
            {
                return _explicitScopeMixin.GetModuleOfType(typeDef);
            }
        }
    }

    public interface IReflectionRootProvider
    {
        IEnumerable<MethodDesc> MethodsWithMetadata { get; }
        IEnumerable<MethodDesc> InvokableMethods { get; }
        IEnumerable<MetadataType> TypesWithMetadata { get; }
        IEnumerable<TypeDesc> InvokableTypes { get; }

        // TODO: same for fields
    }
}
