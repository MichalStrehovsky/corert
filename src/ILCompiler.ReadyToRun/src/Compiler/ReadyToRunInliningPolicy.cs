// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Internal.TypeSystem;

namespace ILCompiler
{
    class ReadyToRunInliningPolicy : InliningPolicy
    {
        private readonly CompilationModuleGroup _compilationModuleGroup;

        public ReadyToRunInliningPolicy(CompilationModuleGroup compilationModuleGroup)
        {
            _compilationModuleGroup = compilationModuleGroup;
        }

        public override bool CanInline(MethodDesc callerMethod, MethodDesc calleeMethod)
        {
            // Allow inlining if the caller is within the current version bubble
            // (because otherwise we may not be able to encode its tokens)
            // and if the callee is either in the same version bubble or is marked as non-versionable.
            bool canInline = _compilationModuleGroup.VersionsWithMethodBody(callerMethod) &&
                (_compilationModuleGroup.VersionsWithMethodBody(calleeMethod) ||
                    calleeMethod.HasCustomAttribute("System.Runtime.Versioning", "NonVersionableAttribute"));

            return canInline;
        }
    }
}
