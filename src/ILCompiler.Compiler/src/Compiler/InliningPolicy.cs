// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Internal.TypeSystem;

namespace ILCompiler
{
    /// <summary>
    /// Controls the inlining of method bodies into the caller's method bodies.
    /// </summary>
    public class InliningPolicy
    {
        /// <summary>
        /// Decide whether a given call may get inlined by JIT.
        /// </summary>
        /// <param name="callerMethod">Calling method the assembly code of is about to receive the callee code</param>
        /// <param name="calleeMethod">The called method to be inlined into the caller</param>
        public virtual bool CanInline(MethodDesc callerMethod, MethodDesc calleeMethod)
        {
            return true;
        }
    }
}
