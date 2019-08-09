using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using ILCompiler;
using ILCompiler.DependencyAnalysis;
using Internal.IL;
using Internal.Metadata.NativeFormat;
using Internal.TypeSystem;
using Internal.TypeSystem.Ecma;

namespace AnalysisCompile
{
    class Program
    {
        static void Main(string[] args)
        {
            var targetDetails = new TargetDetails(TargetArchitecture.X64, TargetOS.Windows, TargetAbi.CoreRT, SimdVectorLength.Vector128Bit);
            CompilerTypeSystemContext typeSystemContext = new ReadyToRunCompilerContext(targetDetails, SharedGenericsMode.CanonicalReferenceTypes);

            var inputFilePaths = new Dictionary<string, string>();
            var referenceFilePaths = new Dictionary<string, string>();

            const string inputFile = @"D:\SizeInvestigation\WebApi\bin\Release\netcoreapp2.1\win-x64\publish\AspNetCore.dll";
            AppendExpandedPaths(inputFilePaths, inputFile);
            const string referenceFiles = @"D:\SizeInvestigation\WebApi\bin\Release\netcoreapp2.1\win-x64\publish\*.dll";
            AppendExpandedPaths(referenceFilePaths, referenceFiles);

            typeSystemContext.InputFilePaths = inputFilePaths;
            typeSystemContext.ReferenceFilePaths = referenceFilePaths;

            typeSystemContext.SetSystemModule(typeSystemContext.GetModuleForSimpleName("System.Private.CoreLib"));

            ILProvider ilProvider = new ReadyToRunILProvider();

            HashSet<MethodDesc> methodsCompiled;

            //
            // Run scanner
            //
            {
                CompilationModuleGroup compilationGroup = new SingleFileCompilationModuleGroup();

                MetadataBlockingPolicy mdBlockingPolicy = new BlockedInternalsBlockingPolicy(typeSystemContext);

                ManifestResourceBlockingPolicy resBlockingPolicy = new NoManifestResourceBlockingPolicy();

                var stackTracePolicy = new NoStackTraceEmissionPolicy();

                UsageBasedMetadataGenerationOptions metadataGenerationOptions =
                    UsageBasedMetadataGenerationOptions.AnonymousTypeHeuristic
                    | UsageBasedMetadataGenerationOptions.ILScanning
                    | UsageBasedMetadataGenerationOptions.FullUserAssemblyRooting;

                DynamicInvokeThunkGenerationPolicy invokeThunkGenerationPolicy = new NoDynamicInvokeThunkGenerationPolicy();

                var metadataManager = new UsageBasedMetadataManager(
                    compilationGroup,
                    typeSystemContext,
                    mdBlockingPolicy,
                    resBlockingPolicy,
                    null,
                    stackTracePolicy,
                    invokeThunkGenerationPolicy,
                    ilProvider,
                    metadataGenerationOptions);

                CompilationBuilder builder = new ReadyToRunCodegenCompilationBuilder(typeSystemContext, compilationGroup, inputFile);

                builder.UseILProvider(ilProvider)
                    .UseCompilationUnitPrefix("");

                var compilationRoot = new SingleMethodRootProvider(((EcmaModule)typeSystemContext.GetModuleForSimpleName(Path.GetFileNameWithoutExtension(inputFile))).EntryPoint);

                ILScannerBuilder scannerBuilder = builder.GetILScannerBuilder()
                    .UseCompilationRoots(new[] { compilationRoot })
                    .UseMetadataManager(metadataManager);

                IILScanner scanner = scannerBuilder.ToILScanner();

                var scanResults = scanner.Scan();

                methodsCompiled = new HashSet<MethodDesc>(scanResults.CompiledMethodBodies);
            }

            //
            // Compile
            //

            string directoryName = Path.GetDirectoryName(referenceFiles);
            string searchPattern = Path.GetFileName(referenceFiles);

            foreach (string fileName in Directory.EnumerateFiles(directoryName, searchPattern))
            {
                string fullFileName = Path.GetFullPath(fileName);
                if (fullFileName.EndsWith(".ni.dll"))
                    continue;

                inputFilePaths.Clear();
                AppendExpandedPaths(inputFilePaths, fullFileName);

                EcmaModule module;
                try
                {
                    module = typeSystemContext.GetModuleFromPath(fullFileName);
                }
                catch (TypeSystemException)
                {
                    continue;
                }

                {
                    var compilationRoots = new[] { new FilteredRootProvider(module, methodsCompiled) };

                    var compilationGroup = new ReadyToRunSingleAssemblyCompilationModuleGroup(
                            typeSystemContext, new[] { module }, Array.Empty<ModuleDesc>());

                    var builder = new ReadyToRunCodegenCompilationBuilder(typeSystemContext, compilationGroup, fullFileName);

                    var metadataManager = new ReadyToRunTableManager(typeSystemContext);
                    var interopStubManager = new EmptyInteropStubManager();

                    builder
                        .UseCompilationUnitPrefix("")
                        .UseMetadataManager(metadataManager)
                        .UseInteropStubManager(interopStubManager)
                        .UseCompilationRoots(compilationRoots)
                        .UseOptimizationMode(OptimizationMode.Blended);

                    var compilation = builder.ToCompilation();

                    var outputFileName = Path.Combine(@"D:\SizeInvestigation\CPAOT_Analyzed", Path.GetFileNameWithoutExtension(fullFileName) + ".dll");
                    CompilationResults compilationResults = compilation.Compile(outputFileName, null);

                    Console.WriteLine($"AAA1 {Path.GetFileNameWithoutExtension(fullFileName)}\t{compilationResults.CompiledMethodBodies.Count()}");
                }

                {
                    var compilationRoots = new[] { new ReadyToRunRootProvider(module) };

                    var compilationGroup = new ReadyToRunSingleAssemblyCompilationModuleGroup(
                            typeSystemContext, new[] { module }, Array.Empty<ModuleDesc>());

                    var builder = new ReadyToRunCodegenCompilationBuilder(typeSystemContext, compilationGroup, fullFileName);

                    var metadataManager = new ReadyToRunTableManager(typeSystemContext);
                    var interopStubManager = new EmptyInteropStubManager();

                    builder
                        .UseCompilationUnitPrefix("")
                        .UseMetadataManager(metadataManager)
                        .UseInteropStubManager(interopStubManager)
                        .UseCompilationRoots(compilationRoots)
                        .UseOptimizationMode(OptimizationMode.Blended);

                    var compilation = builder.ToCompilation();

                    var outputFileName = Path.Combine(@"D:\SizeInvestigation\CPAOT_Compiled", Path.GetFileNameWithoutExtension(fullFileName) + ".dll");
                    CompilationResults compilationResults = compilation.Compile(outputFileName, null);

                    Console.WriteLine($"AAA2 {Path.GetFileNameWithoutExtension(fullFileName)}\t{compilationResults.CompiledMethodBodies.Count()}");
                }
            }
        }

        public static void AppendExpandedPaths(Dictionary<string, string> dictionary, string pattern)
        {
            string directoryName = Path.GetDirectoryName(pattern);
            string searchPattern = Path.GetFileName(pattern);

            if (directoryName == "")
                directoryName = ".";

            if (Directory.Exists(directoryName))
            {
                foreach (string fileName in Directory.EnumerateFiles(directoryName, searchPattern))
                {
                    string fullFileName = Path.GetFullPath(fileName);

                    string simpleName = Path.GetFileNameWithoutExtension(fileName);

                    dictionary[simpleName] = fullFileName;
                }
            }
        }
    }

    class FilteredRootProvider : ICompilationRootProvider
    {
        private readonly EcmaModule _ecmaModule;
        private readonly HashSet<MethodDesc> _compiledMethods;

        public FilteredRootProvider(EcmaModule module, HashSet<MethodDesc> methods)
        {
            _ecmaModule = module;
            _compiledMethods = methods;
        }

        public void AddCompilationRoots(IRootingServiceProvider rootProvider)
        {
            ReadyToRunRootProvider readyToRunRootProvider = new ReadyToRunRootProvider(_ecmaModule);
            readyToRunRootProvider.AddCompilationRoots(new Filter(_compiledMethods, rootProvider));
        }

        class Filter : IRootingServiceProvider
        {
            private HashSet<MethodDesc> _compiledMethods;
            private IRootingServiceProvider _wrapped;

            public Filter(HashSet<MethodDesc> compiledMethods, IRootingServiceProvider wrapped)
            {
                _compiledMethods = compiledMethods;
                _wrapped = wrapped;
            }

            public void AddCompilationRoot(MethodDesc method, string reason, string exportName = null)
            {
                if (_compiledMethods.Contains(method))
                    _wrapped.AddCompilationRoot(method, reason, exportName);
            }

            public void AddCompilationRoot(TypeDesc type, string reason)
            {
                _wrapped.AddCompilationRoot(type, reason);
            }

            public void RootDelegateMarshallingData(DefType type, string reason)
            {
                throw new NotImplementedException();
            }

            public void RootGCStaticBaseForType(TypeDesc type, string reason)
            {
                throw new NotImplementedException();
            }

            public void RootModuleMetadata(ModuleDesc module, string reason)
            {
                throw new NotImplementedException();
            }

            public void RootNonGCStaticBaseForType(TypeDesc type, string reason)
            {
                throw new NotImplementedException();
            }

            public void RootReadOnlyDataBlob(byte[] data, int alignment, string reason, string exportName)
            {
                throw new NotImplementedException();
            }

            public void RootStructMarshallingData(DefType type, string reason)
            {
                throw new NotImplementedException();
            }

            public void RootThreadStaticBaseForType(TypeDesc type, string reason)
            {
                throw new NotImplementedException();
            }

            public void RootVirtualMethodForReflection(MethodDesc method, string reason)
            {
                throw new NotImplementedException();
            }
        }
    }
}
