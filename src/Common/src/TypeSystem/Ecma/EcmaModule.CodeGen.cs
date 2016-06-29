// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace Internal.TypeSystem.Ecma
{
    partial class EcmaModule
    {
        public override bool IsExe
        {
            get
            {
                return PEReader.PEHeaders.IsExe;
            }
        }

        public override MethodDesc EntryPointMethod
        {
            get
            {
                CorHeader corHeader = PEReader.PEHeaders.CorHeader;
                if ((corHeader.Flags & CorFlags.NativeEntryPoint) != 0)
                {
                    throw new NotSupportedException();
                }

                int entryPointToken = corHeader.EntryPointTokenOrRelativeVirtualAddress;
                if (entryPointToken == 0)
                {
                    return null;
                }

                EntityHandle handle = MetadataTokens.EntityHandle(entryPointToken);

                if (handle.Kind == HandleKind.MethodDefinition)
                {
                    return GetMethod(handle);
                }
                else if (handle.Kind == HandleKind.AssemblyFile)
                {
                    // Entrypoint not in the manifest assembly
                    throw new NotImplementedException();
                }

                throw new BadImageFormatException();
            }
        }

        public override IEnumerable<MethodDesc> ExportedMethods
        {
            get
            {
                MetadataStringComparer stringComparer = MetadataReader.StringComparer;

                foreach (var methodHandle in MetadataReader.MethodDefinitions)
                {
                    foreach (var customAttributeHandle in MetadataReader.GetCustomAttributes(methodHandle))
                    {
                        StringHandle namespaceHandle, nameHandle;
                        if (!MetadataReader.GetAttributeNamespaceAndName(customAttributeHandle, out namespaceHandle, out nameHandle))
                            continue;

                        if ((stringComparer.Equals(nameHandle, "NativeCallableAttribute")
                            && stringComparer.Equals(namespaceHandle, "System.Runtime.InteropServices"))
                            ||
                            (stringComparer.Equals(nameHandle, "RuntimeExportAttribute")
                            && stringComparer.Equals(namespaceHandle, "System.Runtime")))
                        {
                            yield return GetMethod(methodHandle);
                            break;
                        }
                    }       
                }
            }
        }
    }
}
