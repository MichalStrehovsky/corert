// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;

namespace Internal.TypeSystem
{
    partial class ModuleDesc
    {
        /// <summary>
        /// Gets a value indicating whether this is an executable module.
        /// </summary>
        public virtual bool IsExe
        {
            get
            {
                return false;
            }
        }

        /// <summary>
        /// Gets the entrypoint method declared by this module or null.
        /// </summary>
        public virtual MethodDesc EntryPointMethod
        {
            get
            {
                return null;
            }
        }

        /// <summary>
        /// Gets the list of native callable methods and runtime exported methods in the module.
        /// </summary>
        public virtual IEnumerable<MethodDesc> ExportedMethods
        {
            get
            {
                foreach (var type in GetAllTypes())
                {
                    foreach (var method in type.GetMethods())
                    {
                        if (method.IsRuntimeExport || method.IsNativeCallable)
                        {
                            yield return method;                        
                        }
                    }
                }
            }
        }
    }
}
