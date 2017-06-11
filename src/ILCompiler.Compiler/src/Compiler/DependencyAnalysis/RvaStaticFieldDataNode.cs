// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;

using Internal.TypeSystem;
using Internal.Text;

using Debug = System.Diagnostics.Debug;

namespace ILCompiler.DependencyAnalysis
{
    public class RvaStaticFieldDataNode : ObjectNode, ISymbolDefinitionNode
    {
        private readonly FieldDesc _field;

        public RvaStaticFieldDataNode(FieldDesc field)
        {
            Debug.Assert(field.HasRva);
            _field = field;
        }

        public FieldDesc Field => _field;

        public override ObjectNodeSection Section => ObjectNodeSection.DataSection;
        public override bool StaticDependenciesAreComputed => true;

        public void AppendMangledName(NameMangler nameMangler, Utf8StringBuilder sb)
        {
            sb.Append(nameMangler.GetMangledFieldName(_field));
        }
        public int Offset => 0;
        public override bool IsShareable => true;

        public override ObjectData GetData(NodeFactory factory, bool relocsOnly = false)
        {
            return new ObjectData(
                _field.GetFieldRvaData(),
                Array.Empty<Relocation>(),
                _field.Context.Target.PointerSize,
                new ISymbolDefinitionNode[] { this });
        }

        protected override string GetName(NodeFactory factory) => this.GetMangledName(factory.NameMangler);
    }
}
