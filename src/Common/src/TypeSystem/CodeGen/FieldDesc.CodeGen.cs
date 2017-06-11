// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Debug = System.Diagnostics.Debug;

namespace Internal.TypeSystem
{
    // Additional members of FieldDesc related to code generation.
    partial class FieldDesc
    {
        /// <summary>
        /// Gets the data of an RVA static field.
        /// </summary>
        public virtual byte[] GetFieldRvaData()
        {
            // Sanity check - someone has overriden HasRva but forgot to override this.
            Debug.Assert(!HasRva);
            return null;
        }
    }

    partial class FieldForInstantiatedType
    {
        public override byte[] GetFieldRvaData()
        {
            return _fieldDef.GetFieldRvaData();
        }
    }
}
