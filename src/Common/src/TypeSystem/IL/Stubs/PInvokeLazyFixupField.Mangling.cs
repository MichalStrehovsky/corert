// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Internal.TypeSystem;

namespace Internal.IL.Stubs
{
    partial class PInvokeLazyFixupField : IPrefixMangledMethod
    {
        public MethodDesc BaseMethod => _targetMethod;

        public string Prefix => "PInvokeFixupCell";
    }
}