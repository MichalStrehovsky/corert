// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.IO;
using System.Reflection.PortableExecutable;

using Internal.IL;
using Internal.JitInterface;
using Internal.TypeSystem;
using Internal.TypeSystem.Ecma;

using ILCompiler.DependencyAnalysis;
using ILCompiler.DependencyAnalysis.ReadyToRun;
using ILCompiler.DependencyAnalysisFramework;
using System;

namespace ILCompiler
{
    public sealed class ReadyToRunCodegenCompilation : Compilation
    {
        /// <summary>
        /// JIT configuration provider.
        /// </summary>
        private readonly JitConfigProvider _jitConfigProvider;

        /// <summary>
        /// Name of the compilation input MSIL file.
        /// </summary>
        private readonly string _inputFilePath;

        /// <summary>
        /// JIT interface implementation.
        /// </summary>
        private readonly CorInfoImpl _corInfo;

        public new ReadyToRunCodegenNodeFactory NodeFactory { get; }

        public ReadyToRunSymbolNodeFactory SymbolNodeFactory { get; }

        internal ReadyToRunCodegenCompilation(
            DependencyAnalyzerBase<NodeFactory> dependencyGraph,
            ReadyToRunCodegenNodeFactory nodeFactory,
            IEnumerable<ICompilationRootProvider> roots,
            ILProvider ilProvider,
            DebugInformationProvider debugInformationProvider,
            Logger logger,
            DevirtualizationManager devirtualizationManager,
            JitConfigProvider configProvider,
            string inputFilePath)
            : base(dependencyGraph, nodeFactory, roots, ilProvider, debugInformationProvider, devirtualizationManager, logger)
        {
            NodeFactory = nodeFactory;
            SymbolNodeFactory = new ReadyToRunSymbolNodeFactory(nodeFactory);
            _jitConfigProvider = configProvider;

            _inputFilePath = inputFilePath;
            _corInfo = new CorInfoImpl(this, _jitConfigProvider);
        }

        protected override void CompileInternal(string outputFile, ObjectDumper dumper)
        {
            using (FileStream inputFile = File.OpenRead(_inputFilePath))
            {
                PEReader inputPeReader = new PEReader(inputFile);

                _dependencyGraph.ComputeMarkedNodes();
                var nodes = _dependencyGraph.MarkedNodeList;

                NodeFactory.SetMarkingComplete();
                ReadyToRunObjectWriter.EmitObject(inputPeReader, outputFile, nodes, NodeFactory);
            }

            int totalGenericCodeSize = 0;
            foreach (var markedNode in _dependencyGraph.MarkedNodeList)
            {
                if (markedNode is MethodWithGCInfo methodNode)
                {
                    if (methodNode.IsEmpty)
                        continue;

                    MethodDesc method = methodNode.Method;
                    if (method.HasInstantiation || method.OwningType.HasInstantiation)
                    {
                        var code = methodNode.GetData(NodeFactory, false);
                        var fixups = methodNode.GetFixupBlob(NodeFactory);
                        totalGenericCodeSize += code.Data.Length + fixups?.Length ?? 0;
                    }
                }
            }

            Console.WriteLine($"*** {Path.GetFileName(outputFile)}\t{totalGenericCodeSize}");
        }

        internal bool IsInheritanceChainLayoutFixedInCurrentVersionBubble(TypeDesc type)
        {
            // TODO: implement
            return true;
        }

        public override TypeDesc GetTypeOfRuntimeType()
        {
            return TypeSystemContext.SystemModule.GetKnownType("System", "RuntimeType");
        }

        protected override void ComputeDependencyNodeDependencies(List<DependencyNodeCore<NodeFactory>> obj)
        {
            foreach (DependencyNodeCore<NodeFactory> dependency in obj)
            {
                var methodCodeNodeNeedingCode = dependency as MethodWithGCInfo;
                if (methodCodeNodeNeedingCode == null)
                {
                    // To compute dependencies of the shadow method that tracks dictionary
                    // dependencies we need to ensure there is code for the canonical method body.
                    var dependencyMethod = (ShadowConcreteMethodNode)dependency;
                    methodCodeNodeNeedingCode = (MethodWithGCInfo)dependencyMethod.CanonicalMethodNode;
                }

                // We might have already compiled this method.
                if (methodCodeNodeNeedingCode.StaticDependenciesAreComputed)
                    continue;

                MethodDesc method = methodCodeNodeNeedingCode.Method;
                if (!NodeFactory.CompilationModuleGroup.ContainsMethodBody(method, unboxingStub: false))
                {
                    // Don't drill into methods defined outside of this version bubble
                    continue;
                }

                if (method.HasInstantiation || method.OwningType.HasInstantiation)
                {
                    if (Environment.GetEnvironmentVariable("CPAOT_NO_GENERIC_CODE") == "1")
                    {
                        methodCodeNodeNeedingCode.SetCode(new ObjectNode.ObjectData(Array.Empty<byte>(), null, 1, Array.Empty<ISymbolDefinitionNode>()));
                        methodCodeNodeNeedingCode.InitializeFrameInfos(Array.Empty<FrameInfo>());
                        continue;
                    }

                    if (Environment.GetEnvironmentVariable("CPAOT_ONLY_CANONICAL_CODE") == "1")
                    {
                        bool nonCanon = false;
                        foreach (var a in method.Instantiation)
                        {
                            if (a != NodeFactory.TypeSystemContext.CanonType)
                                nonCanon = true;
                        }
                        foreach (var a in method.OwningType.Instantiation)
                        {
                            if (a != NodeFactory.TypeSystemContext.CanonType)
                                nonCanon = true;
                        }
                        if (nonCanon)
                        {
                            methodCodeNodeNeedingCode.SetCode(new ObjectNode.ObjectData(Array.Empty<byte>(), null, 1, Array.Empty<ISymbolDefinitionNode>()));
                            methodCodeNodeNeedingCode.InitializeFrameInfos(Array.Empty<FrameInfo>());
                            //Logger.Writer.WriteLine($"Info: Method `{method}` skipped");
                            continue;
                        }
                    }
                }

                if (Logger.IsVerbose)
                {
                    string methodName = method.ToString();
                    Logger.Writer.WriteLine("Compiling " + methodName);
                }

                try
                {
                    _corInfo.CompileMethod(methodCodeNodeNeedingCode);
                }
                catch (TypeSystemException)
                {
                    // If compilation fails, don't emit code for this method. It will be Jitted at runtime
                    //Logger.Writer.WriteLine($"Warning: Method `{method}` was not compiled because: {ex.Message}");
                }
                catch (RequiresRuntimeJitException)
                {
                    //Logger.Writer.WriteLine($"Info: Method `{method}` was not compiled because `{ex.Message}` requires runtime JIT");
                }
            }
        }

        public override ISymbolNode GetFieldRvaData(FieldDesc field) => SymbolNodeFactory.GetRvaFieldNode(field);
    }
}
