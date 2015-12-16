// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;

namespace Internal.TypeSystem
{
    public enum GenericParameterKind
    {
        Type,
        Method,
    }

    public abstract partial class GenericParameterDesc : TypeDesc
    {
        /// <summary>
        /// Gets a value indicating whether this is a type or method generic parameter.
        /// </summary>
        public abstract GenericParameterKind Kind { get; }
        
        /// <summary>
        /// Gets the zero based index of the generic parameter within the declaring type or method.
        /// </summary>
        public abstract int Index { get; }

        /// <summary>
        /// Gets a value indicating whether this parameter is covariant.
        /// </summary>
        public abstract bool IsCovariant { get; }

        /// <summary>
        /// Gets a value indicating whether this parameter is contravariant.
        /// </summary>
        public abstract bool IsContravariant { get; }

        /// <summary>
        /// Gets a value indicating whether substitutions need to have a default constructor.
        /// </summary>
        public abstract bool HasDefaultConstructorConstraint { get; }

        /// <summary>
        /// Gets a value indicating whether substitutions need to be reference types.
        /// </summary>
        public abstract bool HasReferenceTypeConstraint { get; }

        /// <summary>
        /// Gets a value indicating whether substitutions need be not nullable value types.
        /// </summary>
        public abstract bool HasValueTypeConstraint { get; }

        /// <summary>
        /// Gets type constraints imposed on substitutions.
        /// </summary>
        public abstract IEnumerable<TypeDesc> Constraints { get; }
    }
}
