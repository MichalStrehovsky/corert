// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Internal.TypeSystem
{
    public class CachingVirtualMethodAlgorithm : VirtualMethodAlgorithm
    {
#if VALIDATE
        MetadataVirtualMethodAlgorithm backup = new MetadataVirtualMethodAlgorithm();
#endif

        public CachingVirtualMethodAlgorithm()
        {
            _vtableHashTable = new VTableHashTable(this);
        }

        public override IEnumerable<MethodDesc> ComputeAllVirtualSlots(TypeDesc type)
        {
            MetadataType currentType = (MetadataType)type;

            do
            {
                IntroducedSlotList slotList = _introducedSlotCache.GetOrCreateValue(currentType);

                foreach (MethodDesc slot in slotList.IntroducedSlots)
                {
                    yield return slot;
                }

                currentType = currentType.MetadataBaseType;
            }
            while (currentType != null);
        }

        public override MethodDesc FindVirtualFunctionTargetMethodOnObjectType(MethodDesc targetMethod, TypeDesc objectType)
        {
            MethodDesc slotDefiningMethodDefinition = MetadataVirtualMethodAlgorithm.FindSlotDefiningMethodForVirtualMethod(targetMethod).GetTypicalMethodDefinition();
            TypeDesc slotOwningTypeDefinition = slotDefiningMethodDefinition.OwningType;

            MetadataType currentType = (MetadataType)objectType.GetTypeDefinition();

            int slotOffset = 0;
            VTable vtable = _vtableHashTable.GetOrCreateValue((MetadataType)objectType);

            IntroducedSlotList currentSlotList = _introducedSlotCache.GetOrCreateValue(currentType);
            while (currentType != slotOwningTypeDefinition && currentType != null)
            {
                slotOffset += currentSlotList.IntroducedSlots.Length;

                currentType = (MetadataType)currentType.MetadataBaseType?.GetTypeDefinition();
                currentSlotList = currentType == null ? null : _introducedSlotCache.GetOrCreateValue(currentType);            
            }

            if (currentType == null)
            {
#if VALIDATE
                Debug.Assert(backup.FindVirtualFunctionTargetMethodOnObjectType(targetMethod, objectType) == null);
#endif
                return null;
            }

            foreach (MethodDesc decl in currentSlotList.IntroducedSlots)
            {
                if (decl == slotDefiningMethodDefinition)
                {
                    MethodDesc impl = vtable.ImplSlots[slotOffset];
                    if (targetMethod.HasInstantiation && !targetMethod.IsGenericMethodDefinition)
                    {
                        impl = impl.MakeInstantiatedMethod(targetMethod.Instantiation);
                    }

#if VALIDATE
                    MethodDesc expected = backup.FindVirtualFunctionTargetMethodOnObjectType(targetMethod, objectType);
                    Debug.Assert(expected == impl);
#endif
                    return impl;
                }

                slotOffset++;
            }

#if VALIDATE
            Debug.Assert(backup.FindVirtualFunctionTargetMethodOnObjectType(targetMethod, objectType) == null);
#endif
            return null;
        }

        public override MethodDesc ResolveInterfaceMethodToVirtualMethodOnType(MethodDesc interfaceMethod, TypeDesc currentType)
        {
            return MetadataVirtualMethodAlgorithm.ResolveInterfaceMethodToVirtualMethodOnType(interfaceMethod, (MetadataType)currentType);
        }

        public override MethodDesc ResolveVariantInterfaceMethodToVirtualMethodOnType(MethodDesc interfaceMethod, TypeDesc currentType)
        {
            return MetadataVirtualMethodAlgorithm.ResolveVariantInterfaceMethodToVirtualMethodOnType(interfaceMethod, (MetadataType)currentType);
        }

        private readonly IntroducedVirtualSlotHashTable _introducedSlotCache = new IntroducedVirtualSlotHashTable();
        private class IntroducedVirtualSlotHashTable : LockFreeReaderHashtable<MetadataType, IntroducedSlotList>
        {
            protected override bool CompareKeyToValue(MetadataType key, IntroducedSlotList value)
            {
                return key == value.Slice;
            }

            protected override bool CompareValueToValue(IntroducedSlotList value1, IntroducedSlotList value2)
            {
                return value1.Slice == value2.Slice;
            }

            protected override int GetKeyHashCode(MetadataType key)
            {
                return key.GetHashCode();
            }

            protected override int GetValueHashCode(IntroducedSlotList value)
            {
                return value.Slice.GetHashCode();
            }

            protected override IntroducedSlotList CreateValueFromKey(MetadataType key)
            {
                ArrayBuilder<MethodDesc> builder = new ArrayBuilder<MethodDesc>();

                foreach (MethodDesc m in key.GetAllMethods())
                {
                    if (!m.IsVirtual)
                        continue;

                    MethodDesc possibleNewSlot = MetadataVirtualMethodAlgorithm.FindSlotDefiningMethodForVirtualMethod(m);
                    if (possibleNewSlot.OwningType == key)
                        builder.Add(possibleNewSlot);
                }

                return new IntroducedSlotList(key, builder.ToArray());
            }
        }

        private class IntroducedSlotList
        {
            public readonly MetadataType Slice;
            public readonly MethodDesc[] IntroducedSlots;

            public IntroducedSlotList(MetadataType slice, MethodDesc[] introducedSlots)
            {
                Slice = slice;
                IntroducedSlots = introducedSlots;
            }
        }

        private readonly VTableHashTable _vtableHashTable;
        class VTableHashTable : LockFreeReaderHashtable<MetadataType, VTable>
        {
            private readonly CachingVirtualMethodAlgorithm _parent;

            public VTableHashTable(CachingVirtualMethodAlgorithm parent)
            {
                _parent = parent;
            }

            protected override bool CompareKeyToValue(MetadataType key, VTable value)
            {
                return key == value.OwningType;
            }

            protected override bool CompareValueToValue(VTable value1, VTable value2)
            {
                return value1.OwningType == value2.OwningType;
            }

            protected override int GetKeyHashCode(MetadataType key)
            {
                return key.GetHashCode();
            }

            protected override int GetValueHashCode(VTable value)
            {
                return value.OwningType.GetHashCode();
            }

            protected override VTable CreateValueFromKey(MetadataType key)
            {
                ArrayBuilder<MethodDesc> implSlots = new ArrayBuilder<MethodDesc>();

                MetadataType currentType = (MetadataType)key.GetTypeDefinition();
                do
                {
                    IntroducedSlotList introducedList = _parent._introducedSlotCache.GetOrCreateValue(currentType);

                    foreach (MethodDesc decl in introducedList.IntroducedSlots)
                    {
                        MethodDesc instantiatedDecl = key.FindMethodOnTypeWithMatchingTypicalMethod(decl);
                        MethodDesc impl = MetadataVirtualMethodAlgorithm.FindVirtualFunctionTargetMethodOnObjectType(instantiatedDecl, key);
                        implSlots.Add(impl);
                    }

                    currentType = currentType.MetadataBaseType;
                }
                while (currentType != null);

                return new VTable(key, implSlots.ToArray());
            }
        }

        private class VTable
        {
            public readonly MetadataType OwningType;
            public readonly MethodDesc[] ImplSlots;

            public VTable(MetadataType owningType, MethodDesc[] implSlots)
            {
                OwningType = owningType;
                ImplSlots = implSlots;
            }
        }
    }
}
