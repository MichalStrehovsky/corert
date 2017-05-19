// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Internal.TypeSystem;

namespace ILCompiler
{
    /// <summary>
    /// Represents a metadata blocking policy. A metadata blocking policy decides what types or members are never
    /// eligible to have their metadata generated into the executable.
    /// </summary>
    public abstract class MetadataBlockingPolicy
    {
        public abstract bool IsBlocked(MetadataType type);
        public abstract bool IsBlocked(MethodDesc method);
    }

    /// <summary>
    /// Represents a metadata policy that blocks implementations details.
    /// </summary>
    public sealed class BlockedInternalsBlockingPolicy : MetadataBlockingPolicy
    {
        private TypeDesc _arrayOfTType;

        private bool IsArrayOfTType(TypeDesc type)
        {
            if (_arrayOfTType == null)
            {
                _arrayOfTType = type.Context.SystemModule.GetType("System", "Array`1");
            }

            return type == _arrayOfTType;
        }

        public override bool IsBlocked(MetadataType type)
        {
            // TODO: Make this also respect System.Runtime.CompilerServices.DisablePrivateReflectionAttribute
            return !(type.GetTypeDefinition() is Internal.TypeSystem.Ecma.EcmaType);
        }

        public override bool IsBlocked(MethodDesc method)
        {
            // TODO: Make this also respect System.Runtime.CompilerServices.DisablePrivateReflectionAttribute
            MethodDesc typicalMethod = method.GetTypicalMethodDefinition();
            if (!(typicalMethod is Internal.TypeSystem.Ecma.EcmaMethod))
                return true;

            if (IsArrayOfTType(typicalMethod.OwningType))
                return true;

            return false;
        }
    }
}
