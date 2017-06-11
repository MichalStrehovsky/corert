// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;
using System.Reflection.Metadata;

namespace Internal.TypeSystem.Ecma
{
    partial class EcmaField
    {
        public override byte[] GetFieldRvaData()
        {
            Debug.Assert(HasRva);

            int addr = MetadataReader.GetFieldDefinition(Handle).GetRelativeVirtualAddress();
            var memBlock = Module.PEReader.GetSectionData(addr).GetContent();

            int size = FieldType.GetElementSize().AsInt;
            if (size > memBlock.Length)
                throw new TypeSystemException.BadImageFormatException();

            byte[] result = new byte[size];
            memBlock.CopyTo(0, result, 0, result.Length);

            return result;
        }
    }
}
