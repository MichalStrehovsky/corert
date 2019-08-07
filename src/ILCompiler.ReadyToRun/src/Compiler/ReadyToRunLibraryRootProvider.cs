// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Internal.TypeSystem.Ecma;
using Internal.TypeSystem;
using System.Net;
using System;

namespace ILCompiler
{
    /// <summary>
    /// Provides compilation group for a library that compiles everything in the input IL module.
    /// </summary>
    public class ReadyToRunRootProvider : ICompilationRootProvider
    {
        private EcmaModule _module;

        public ReadyToRunRootProvider(EcmaModule module)
        {
            _module = module;
        }

        public void AddCompilationRoots(IRootingServiceProvider rootProvider)
        {
            foreach (TypeDesc type in _module.GetAllTypes())
            {
                try
                {
                    rootProvider.AddCompilationRoot(type, "Library module type");
                }
                catch (TypeSystemException)
                {
                    // Swallow type load exceptions while rooting
                    continue;
                }

                var t = type;
                if (t.HasInstantiation)
                {
                    if (Environment.GetEnvironmentVariable("CPAOT_ROOT_CANONICAL_CODE") != "1")
                        continue;

                    var inst = new TypeDesc[t.Instantiation.Length];
                    for (int i = 0; i < inst.Length; i++)
                    {
                        inst[i] = t.Context.CanonType;
                    }
                    t = ((MetadataType)t).MakeInstantiatedType(inst);
                }

                RootMethods(t, "Library module method", rootProvider);
            }
        }

        private void RootMethods(TypeDesc type, string reason, IRootingServiceProvider rootProvider)
        {
            foreach (MethodDesc method in type.GetAllMethods())
            {
                // Skip methods with no IL and uninstantiated generic methods
                if (method.IsAbstract)
                    continue;

                if (method.HasInstantiation && Environment.GetEnvironmentVariable("CPAOT_ROOT_CANONICAL_CODE") != "1")
                    continue;

                MethodDesc m = method;
                if (m.HasInstantiation)
                {
                    var inst = new TypeDesc[m.Instantiation.Length];
                    for (int i = 0; i < inst.Length; i++)
                    {
                        inst[i] = m.Context.CanonType;
                    }
                    m = m.MakeInstantiatedMethod(inst);
                }

                if (m.IsInternalCall)
                    continue;

                try
                {
                    CheckCanGenerateMethod(m);
                    rootProvider.AddCompilationRoot(m, reason);
                }
                catch (TypeSystemException)
                {
                    // Individual methods can fail to load types referenced in their signatures.
                    // Skip them in library mode since they're not going to be callable.
                    continue;
                }
            }
        }

        /// <summary>
        /// Validates that it will be possible to generate '<paramref name="method"/>' based on the types 
        /// in its signature. Unresolvable types in a method's signature prevent RyuJIT from generating
        /// even a stubbed out throwing implementation.
        /// </summary>
        public static void CheckCanGenerateMethod(MethodDesc method)
        {
            MethodSignature signature = method.Signature;

            // Vararg methods are not supported in .NET Core
            if ((signature.Flags & MethodSignatureFlags.UnmanagedCallingConventionMask) == MethodSignatureFlags.CallingConventionVarargs)
                ThrowHelper.ThrowBadImageFormatException();

            CheckTypeCanBeUsedInSignature(signature.ReturnType);

            for (int i = 0; i < signature.Length; i++)
            {
                CheckTypeCanBeUsedInSignature(signature[i]);
            }
        }

        private static void CheckTypeCanBeUsedInSignature(TypeDesc type)
        {
            MetadataType defType = type as MetadataType;

            if (defType != null)
            {
                defType.ComputeTypeContainsGCPointers();
            }
        }
    }
}
