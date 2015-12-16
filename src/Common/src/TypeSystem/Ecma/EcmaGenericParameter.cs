// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Reflection.Metadata;
using Internal.NativeFormat;

using Debug = System.Diagnostics.Debug;
using GenericParameterAttributes = System.Reflection.GenericParameterAttributes;

namespace Internal.TypeSystem.Ecma
{
    public sealed partial class EcmaGenericParameter : GenericParameterDesc
    {
        private EcmaModule _module;
        private GenericParameterHandle _handle;

        internal EcmaGenericParameter(EcmaModule module, GenericParameterHandle handle)
        {
            _module = module;
            _handle = handle;
        }

        public override int GetHashCode()
        {
            // TODO: Determine what a the right hash function should be. Use stable hashcode based on the type name?
            // For now, use the same hash as a SignatureVariable type.
            GenericParameter parameter = _module.MetadataReader.GetGenericParameter(_handle);
            return TypeHashingAlgorithms.ComputeSignatureVariableHashCode(parameter.Index, parameter.Parent.Kind == HandleKind.MethodDefinition);
        }

        public override TypeSystemContext Context
        {
            get
            {
                return _module.Context;
            }
        }

        protected override TypeFlags ComputeTypeFlags(TypeFlags mask)
        {
            TypeFlags flags = 0;

            flags |= TypeFlags.ContainsGenericVariablesComputed | TypeFlags.ContainsGenericVariables;

            flags |= TypeFlags.GenericParameter;

            Debug.Assert((flags & mask) != 0);
            return flags;
        }

        public override TypeDesc InstantiateSignature(Instantiation typeInstantiation, Instantiation methodInstantiation)
        {
            GenericParameter parameter = _module.MetadataReader.GetGenericParameter(_handle);
            if (parameter.Parent.Kind == HandleKind.MethodDefinition)
            {
                return methodInstantiation[parameter.Index];
            }
            else
            {
                Debug.Assert(parameter.Parent.Kind == HandleKind.TypeDefinition);
                return typeInstantiation[parameter.Index];
            }
        }

        public override GenericParameterKind Kind
        {
            get
            {
                GenericParameter parameter = _module.MetadataReader.GetGenericParameter(_handle);
                if (parameter.Parent.Kind == HandleKind.MethodDefinition)
                {
                    return GenericParameterKind.Method;
                }
                else
                {
                    Debug.Assert(parameter.Parent.Kind == HandleKind.TypeDefinition);
                    return GenericParameterKind.Type;
                }
            }
        }

        public override int Index
        {
            get
            {
                GenericParameter parameter = _module.MetadataReader.GetGenericParameter(_handle);
                return parameter.Index;
            }
        }

        public override bool IsCovariant
        {
            get
            {
                GenericParameter parameter = _module.MetadataReader.GetGenericParameter(_handle);
                return (parameter.Attributes & GenericParameterAttributes.Covariant) != 0;
            }
        }

        public override bool IsContravariant
        {
            get
            {
                GenericParameter parameter = _module.MetadataReader.GetGenericParameter(_handle);
                return (parameter.Attributes & GenericParameterAttributes.Contravariant) != 0;
            }
        }

        public override bool HasReferenceTypeConstraint
        {
            get
            {
                GenericParameter parameter = _module.MetadataReader.GetGenericParameter(_handle);
                return (parameter.Attributes & GenericParameterAttributes.ReferenceTypeConstraint) != 0;
            }
        }

        public override bool HasDefaultConstructorConstraint
        {
            get
            {
                GenericParameter parameter = _module.MetadataReader.GetGenericParameter(_handle);
                return (parameter.Attributes & GenericParameterAttributes.DefaultConstructorConstraint) != 0;
            }
        }

        public override bool HasValueTypeConstraint
        {
            get
            {
                GenericParameter parameter = _module.MetadataReader.GetGenericParameter(_handle);
                return (parameter.Attributes & GenericParameterAttributes.NotNullableValueTypeConstraint) != 0;
            }
        }

        public override IEnumerable<TypeDesc> Constraints
        {
            get
            {
                MetadataReader reader = _module.MetadataReader;

                GenericParameter parameter = reader.GetGenericParameter(_handle);
                GenericParameterConstraintHandleCollection constraintHandles = parameter.GetConstraints();
                TypeDesc[] constraintTypes = new TypeDesc[constraintHandles.Count];

                for (int i = 0; i < constraintTypes.Length; i++)
                {
                    GenericParameterConstraint constraint = reader.GetGenericParameterConstraint(constraintHandles[i]);
                    constraintTypes[i] = _module.GetType(constraint.Type);
                };

                return constraintTypes;
            }
        }

#if CCIGLUE
        public TypeDesc DefiningType
        {
            get
            {
                var genericParameter = _module.MetadataReader.GetGenericParameter(_handle);
                return _module.GetObject(genericParameter.Parent) as TypeDesc;
            }
        }

        public MethodDesc DefiningMethod
        {
            get
            {
                var genericParameter = _module.MetadataReader.GetGenericParameter(_handle);
                return _module.GetObject(genericParameter.Parent) as MethodDesc;
            }
        }
#endif
    }
}
