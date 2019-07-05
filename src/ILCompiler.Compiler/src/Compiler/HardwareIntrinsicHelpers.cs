// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Diagnostics;

using Internal.TypeSystem;
using Internal.IL;
using Internal.IL.Stubs;

namespace ILCompiler
{
    /// <summary>
    /// Helper class that wraps hardware intrinsic code generation policies.
    /// </summary>
    /// <remarks>
    /// Hardware intrinsic classes have multiple code generation strategies:
    /// 
    /// * We either assume that a particular ISA feature is always present/not present
    ///   at runtime and we generate the particular IsSupported methods (e.g. Avx2.IsSupported)
    ///   as always returning true/false.
    /// * If the codegen backend supports it, we can also choose to generate IsSupported
    ///   methods as a runtime check. Runtime check could be generated in various ways.
    ///   One of the ways is to generate a method body for IsSupported that consults
    ///   a field that was initialized based on a CPUID check at process startup.
    ///   
    /// This class wraps the policies around different intrinsics classes on different
    /// CPU architectures.
    /// </remarks>
    public abstract class HardwareIntrinsicHelper
    {
        /// <summary>
        /// Gets a value indicating whether this is a hardware intrinsic on the platform that we're compiling for.
        /// </summary>
        public abstract bool IsHardwareIntrinsic(MethodDesc method);

        /// <summary>
        /// Gets a value indicating whether the `IsSupported` property value for a given intrinsic method class
        /// is known at compile time.
        /// </summary>
        public abstract bool HasKnownSupportLevelAtCompileTime(MethodDesc method);

        /// <summary>
        /// Gets the bit that corresponds to the ISA feature required by the intrinsic
        /// '<paramref name="method"/>'.
        /// </summary>
        protected abstract bool TryGetSupportFlag(MethodDesc method, out int bit);

        /// <summary>
        /// Gets a value indicating whether <paramref name="method"/> is a `IsSupported` method.
        /// </summary>
        public static bool IsIsSupportedMethod(MethodDesc method)
        {
            return method.Name == "get_IsSupported";
        }

        public static MethodIL GetUnsupportedImplementationIL(MethodDesc method)
        {
            // The implementation of IsSupported for codegen backends that don't support hardware intrinsics
            // at all is to return 0.
            if (IsIsSupportedMethod(method))
            {
                return new ILStubMethodIL(method,
                    new byte[] {
                        (byte)ILOpcode.ldc_i4_0,
                        (byte)ILOpcode.ret
                    },
                    Array.Empty<LocalVariableDefinition>(), null);
            }

            // Other methods throw PlatformNotSupportedException
            MethodDesc throwPnse = method.Context.GetHelperEntryPoint("ThrowHelpers", "ThrowPlatformNotSupportedException");

            return new ILStubMethodIL(method,
                    new byte[] {
                        (byte)ILOpcode.call, 1, 0, 0, 0,
                        (byte)ILOpcode.br_s, unchecked((byte)-7),
                    },
                    Array.Empty<LocalVariableDefinition>(),
                    new object[] { throwPnse });
        }

        /// <summary>
        /// Generates IL for the IsSupported property that reads this information from a field initialized by the runtime
        /// at startup. Returns null for hardware intrinsics whose support level is known at compile time
        /// (i.e. they're known to be always supported or always unsupported).
        /// </summary>
        public MethodIL EmitIsSupportedIL(MethodDesc method, FieldDesc isSupportedField)
        {
            Debug.Assert(IsIsSupportedMethod(method));
            Debug.Assert(isSupportedField.IsStatic && isSupportedField.FieldType.IsWellKnownType(WellKnownType.Int32));

            if (!TryGetSupportFlag(method, out int flag))
                return null;

            var emit = new ILEmitter();
            ILCodeStream codeStream = emit.NewCodeStream();

            // return (g_isSupportedField & flag) != 0;
            codeStream.Emit(ILOpcode.ldsfld, emit.NewToken(isSupportedField));
            codeStream.EmitLdc(flag);
            codeStream.Emit(ILOpcode.and);
            codeStream.EmitLdc(0);
            codeStream.Emit(ILOpcode.cgt_un);
            codeStream.Emit(ILOpcode.ret);

            return emit.Link(method);
        }
    }
}
