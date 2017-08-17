// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;

namespace Internal.TypeSystem
{
    public class PrimitiveCanonLayoutAlgorithm : FieldLayoutAlgorithm
    {
        public static PrimitiveCanonLayoutAlgorithm Instance = new PrimitiveCanonLayoutAlgorithm();

        private PrimitiveCanonLayoutAlgorithm() { }

        public override bool ComputeContainsGCPointers(DefType type)
        {
            return false;
        }

        public override bool ComputeIsByRefLike(DefType type)
        {
            return false;
        }

        public override DefType ComputeHomogeneousFloatAggregateElementType(DefType type)
        {
            return null;
        }

        public override ComputedInstanceFieldLayout ComputeInstanceLayout(DefType type, InstanceLayoutKind layoutKind)
        {
            MetadataFieldLayoutAlgorithm.SizeAndAlignment instanceByteSizeAndAlignment;
            var sizeAndAlignment = MetadataFieldLayoutAlgorithm.ComputeInstanceSize(
                type,
                type.Context.Target.GetWellKnownTypeSize(type),
                type.Context.Target.GetWellKnownTypeAlignment(type),
                out instanceByteSizeAndAlignment
                );

            return new ComputedInstanceFieldLayout
            {
                ByteCountUnaligned = instanceByteSizeAndAlignment.Size,
                ByteCountAlignment = instanceByteSizeAndAlignment.Alignment,
                FieldAlignment = sizeAndAlignment.Alignment,
                FieldSize = sizeAndAlignment.Size,
                Offsets = Array.Empty<FieldAndOffset>(),
            };
        }

        public override ComputedStaticFieldLayout ComputeStaticFieldLayout(DefType type, StaticLayoutKind layoutKind)
        {
            return new ComputedStaticFieldLayout()
            {
                NonGcStatics = new StaticsBlock() { Size = LayoutInt.Zero, LargestAlignment = LayoutInt.Zero },
                GcStatics = new StaticsBlock() { Size = LayoutInt.Zero, LargestAlignment = LayoutInt.Zero },
                ThreadStatics = new StaticsBlock() { Size = LayoutInt.Zero, LargestAlignment = LayoutInt.Zero },
                Offsets = Array.Empty<FieldAndOffset>()
            };
        }

        public override ValueTypeShapeCharacteristics ComputeValueTypeShapeCharacteristics(DefType type)
        {
            return ValueTypeShapeCharacteristics.None;
        }
    }
}
