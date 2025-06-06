// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Help;
using System.CommandLine.Parsing;
using System.IO;
using System.Runtime.InteropServices;
using ILCompiler.DependencyAnalysis;
using Internal.TypeSystem;

namespace ILCompiler
{
    internal class Crossgen2RootCommand : RootCommand
    {
        public Argument<Dictionary<string, string>> InputFilePaths { get; } =
            new("input-file-path") { CustomParser = result => Helpers.BuildPathDictionary(result.Tokens, true), Description = "Input file(s)", Arity = ArgumentArity.OneOrMore };
        public Option<Dictionary<string, string>> UnrootedInputFilePaths { get; } =
            new("--unrooted-input-file-paths", "-u") { CustomParser = result => Helpers.BuildPathDictionary(result.Tokens, true), DefaultValueFactory = result => Helpers.BuildPathDictionary(result.Tokens, true), Description = SR.UnrootedInputFilesToCompile };
        public Option<Dictionary<string, string>> ReferenceFilePaths { get; } =
            new("--reference", "-r") { CustomParser = result => Helpers.BuildPathDictionary(result.Tokens, false), DefaultValueFactory = result => Helpers.BuildPathDictionary(result.Tokens, false), Description = SR.ReferenceFiles };
        public Option<string> InstructionSet { get; } =
            new("--instruction-set") { Description = SR.InstructionSets };
        public Option<int> MaxVectorTBitWidth { get; } =
            new("--max-vectort-bitwidth") { Description = SR.MaxVectorTBitWidths };
        public Option<string[]> MibcFilePaths { get; } =
            new("--mibc", "-m") { DefaultValueFactory = _ => Array.Empty<string>(), Description = SR.MibcFiles };
        public Option<string> OutputFilePath { get; } =
            new("--out", "-o") { Description = SR.OutputFilePath };
        public Option<string> CompositeRootPath { get; } =
            new("--compositerootpath", "--crp") { Description = SR.CompositeRootPath };
        public Option<bool> Optimize { get; } =
            new("--optimize", "-O") { Description = SR.EnableOptimizationsOption };
        public Option<bool> OptimizeDisabled { get; } =
            new("--optimize-disabled", "--Od") { Description = SR.DisableOptimizationsOption };
        public Option<bool> OptimizeSpace { get; } =
            new("--optimize-space", "--Os") { Description = SR.OptimizeSpaceOption };
        public Option<bool> OptimizeTime { get; } =
            new("--optimize-time", "--Ot") { Description = SR.OptimizeSpeedOption };
        public Option<bool> EnableCachedInterfaceDispatchSupport { get; } =
            new("--enable-cached-interface-dispatch-support", "--CID") { Description = SR.EnableCachedInterfaceDispatchSupport };
        public Option<TypeValidationRule> TypeValidation { get; } =
            new("--type-validation") { DefaultValueFactory = _ => TypeValidationRule.Automatic, Description = SR.TypeValidation, HelpName = "arg" };
        public Option<bool> InputBubble { get; } =
            new("--inputbubble") { Description = SR.InputBubbleOption };
        public Option<Dictionary<string, string>> InputBubbleReferenceFilePaths { get; } =
            new("--inputbubbleref") { CustomParser = result => Helpers.BuildPathDictionary(result.Tokens, false), DefaultValueFactory = result => Helpers.BuildPathDictionary(result.Tokens, false), Description = SR.InputBubbleReferenceFiles };
        public Option<bool> Composite { get; } =
            new("--composite") { Description = SR.CompositeBuildMode };
        public Option<string> CompositeKeyFile { get; } =
            new("--compositekeyfile") { Description = SR.CompositeKeyFile };
        public Option<bool> CompileNoMethods { get; } =
            new("--compile-no-methods") { Description = SR.CompileNoMethodsOption };
        public Option<bool> OutNearInput { get; } =
            new("--out-near-input") { Description = SR.OutNearInputOption };
        public Option<bool> SingleFileCompilation { get; } =
            new("--single-file-compilation") { Description = SR.SingleFileCompilationOption };
        public Option<bool> Partial { get; } =
            new("--partial") { Description = SR.PartialImageOption };
        public Option<bool> CompileBubbleGenerics { get; } =
            new("--compilebubblegenerics") { Description = SR.BubbleGenericsOption };
        public Option<bool> EmbedPgoData { get; } =
            new("--embed-pgo-data") { Description = SR.EmbedPgoDataOption };
        public Option<string> DgmlLogFileName { get; } =
            new("--dgmllog") { Description = SR.SaveDependencyLogOption };
        public Option<bool> GenerateFullDgmlLog { get; } =
            new("--fulllog") { Description = SR.SaveDetailedLogOption };
        public Option<bool> IsVerbose { get; } =
            new("--verbose") { Description = SR.VerboseLoggingOption };
        public Option<string> SystemModuleName { get; } =
            new("--systemmodule") { DefaultValueFactory = _ => Helpers.DefaultSystemModule, Description = SR.SystemModuleOverrideOption };
        public Option<bool> WaitForDebugger { get; } =
            new("--waitfordebugger") { Description = SR.WaitForDebuggerOption };
        public Option<string[]> CodegenOptions { get; } =
            new("--codegenopt") { DefaultValueFactory = _ => Array.Empty<string>(), Description = SR.CodeGenOptions };
        public Option<bool> SupportIbc { get; } =
            new("--support-ibc") { Description = SR.SupportIbc };
        public Option<bool> Resilient { get; } =
            new("--resilient") { Description = SR.ResilientOption };
        public Option<string> ImageBase { get; } =
            new("--imagebase") { Description = SR.ImageBase };
        public Option<TargetArchitecture> TargetArchitecture { get; } =
            new("--targetarch") { CustomParser = MakeTargetArchitecture, DefaultValueFactory = MakeTargetArchitecture, Description = SR.TargetArchOption, Arity = ArgumentArity.OneOrMore, HelpName = "arg" };
        public Option<bool> EnableGenericCycleDetection { get; } =
            new("--enable-generic-cycle-detection") { Description = SR.EnableGenericCycleDetection };
        public Option<int> GenericCycleDepthCutoff { get; } =
            new("--maxgenericcycle") { DefaultValueFactory = _ => ReadyToRunCompilerContext.DefaultGenericCycleDepthCutoff, Description = SR.GenericCycleDepthCutoff };
        public Option<int> GenericCycleBreadthCutoff { get; } =
            new("--maxgenericcyclebreadth") { DefaultValueFactory = _ => ReadyToRunCompilerContext.DefaultGenericCycleBreadthCutoff, Description = SR.GenericCycleBreadthCutoff };
        public Option<TargetOS> TargetOS { get; } =
            new("--targetos") { CustomParser = result => Helpers.GetTargetOS(result.Tokens.Count > 0 ? result.Tokens[0].Value : null), DefaultValueFactory = result => Helpers.GetTargetOS(result.Tokens.Count > 0 ? result.Tokens[0].Value : null), Description = SR.TargetOSOption, HelpName = "arg" };
        public Option<string> JitPath { get; } =
            new("--jitpath") { Description = SR.JitPathOption };
        public Option<bool> PrintReproInstructions { get; } =
            new("--print-repro-instructions") { Description = SR.PrintReproInstructionsOption };
        public Option<string> SingleMethodTypeName { get; } =
            new("--singlemethodtypename") { Description = SR.SingleMethodTypeName };
        public Option<string> SingleMethodName { get; } =
            new("--singlemethodname") { Description = SR.SingleMethodMethodName };
        public Option<int> SingleMethodIndex { get; } =
            new("--singlemethodindex") { Description = SR.SingleMethodIndex };
        public Option<string[]> SingleMethodGenericArgs { get; } =
            new("--singlemethodgenericarg") { Description = SR.SingleMethodGenericArgs };
        public Option<int> Parallelism { get; } =
            new("--parallelism") { CustomParser = MakeParallelism, DefaultValueFactory = MakeParallelism, Description = SR.ParalellismOption };
        public Option<int> CustomPESectionAlignment { get; } =
            new("--custom-pe-section-alignment") { Description = SR.CustomPESectionAlignmentOption };
        public Option<bool> Map { get; } =
            new("--map") { Description = SR.MapFileOption };
        public Option<bool> MapCsv { get; } =
            new("--mapcsv") { Description = SR.MapCsvFileOption };
        public Option<bool> Pdb { get; } =
            new("--pdb") { Description = SR.PdbFileOption };
        public Option<string> PdbPath { get; } =
            new("--pdb-path") { Description = SR.PdbFilePathOption };
        public Option<bool> PerfMap { get; } =
            new("--perfmap") { Description = SR.PerfMapFileOption };
        public Option<string> PerfMapPath { get; } =
            new("--perfmap-path") { Description = SR.PerfMapFilePathOption };
        public Option<int> PerfMapFormatVersion { get; } =
            new("--perfmap-format-version") { DefaultValueFactory = _ => 0, Description = SR.PerfMapFormatVersionOption };
        public Option<string[]> CrossModuleInlining { get; } =
            new("--opt-cross-module") { Description = SR.CrossModuleInlining };
        public Option<bool> AsyncMethodOptimization { get; } =
            new("--opt-async-methods") { Description = SR.AsyncModuleOptimization };
        public Option<string> NonLocalGenericsModule { get; } =
            new("--non-local-generics-module") { DefaultValueFactory = _ => string.Empty, Description = SR.NonLocalGenericsModule };
        public Option<MethodLayoutAlgorithm> MethodLayout { get; } =
            new("--method-layout") { CustomParser = MakeMethodLayoutAlgorithm, DefaultValueFactory = MakeMethodLayoutAlgorithm, Description = SR.MethodLayoutOption, HelpName = "arg" };
        public Option<FileLayoutAlgorithm> FileLayout { get; } =
            new("--file-layout") { CustomParser = MakeFileLayoutAlgorithm, DefaultValueFactory = MakeFileLayoutAlgorithm, Description = SR.FileLayoutOption, HelpName = "arg" };
        public Option<bool> VerifyTypeAndFieldLayout { get; } =
            new("--verify-type-and-field-layout") { Description = SR.VerifyTypeAndFieldLayoutOption };
        public Option<string> CallChainProfileFile { get; } =
            new("--callchain-profile") { Description = SR.CallChainProfileFile };
        public Option<string> MakeReproPath { get; } =
            new("--make-repro-path") { Description = "Path where to place a repro package" };
        public Option<bool> HotColdSplitting { get; } =
            new("--hot-cold-splitting") { Description = SR.HotColdSplittingOption };
        public Option<bool> SynthesizeRandomMibc { get; } =
            new("--synthesize-random-mibc");

        public Option<int> DeterminismStress { get; } =
            new("--determinism-stress");

        public bool CompositeOrInputBubble { get; private set; }
        public OptimizationMode OptimizationMode { get; private set; }
        public ParseResult Result { get; private set; }

        public static bool IsArmel { get; private set; }

        public Crossgen2RootCommand(string[] args) : base(SR.Crossgen2BannerText)
        {
            Arguments.Add(InputFilePaths);
            Options.Add(UnrootedInputFilePaths);
            Options.Add(ReferenceFilePaths);
            Options.Add(InstructionSet);
            Options.Add(MaxVectorTBitWidth);
            Options.Add(MibcFilePaths);
            Options.Add(OutputFilePath);
            Options.Add(CompositeRootPath);
            Options.Add(Optimize);
            Options.Add(OptimizeDisabled);
            Options.Add(OptimizeSpace);
            Options.Add(OptimizeTime);
            Options.Add(EnableCachedInterfaceDispatchSupport);
            Options.Add(TypeValidation);
            Options.Add(InputBubble);
            Options.Add(InputBubbleReferenceFilePaths);
            Options.Add(Composite);
            Options.Add(CompositeKeyFile);
            Options.Add(CompileNoMethods);
            Options.Add(OutNearInput);
            Options.Add(SingleFileCompilation);
            Options.Add(Partial);
            Options.Add(CompileBubbleGenerics);
            Options.Add(EmbedPgoData);
            Options.Add(DgmlLogFileName);
            Options.Add(GenerateFullDgmlLog);
            Options.Add(IsVerbose);
            Options.Add(SystemModuleName);
            Options.Add(WaitForDebugger);
            Options.Add(CodegenOptions);
            Options.Add(SupportIbc);
            Options.Add(Resilient);
            Options.Add(ImageBase);
            Options.Add(EnableGenericCycleDetection);
            Options.Add(GenericCycleDepthCutoff);
            Options.Add(GenericCycleBreadthCutoff);
            Options.Add(TargetArchitecture);
            Options.Add(TargetOS);
            Options.Add(JitPath);
            Options.Add(PrintReproInstructions);
            Options.Add(SingleMethodTypeName);
            Options.Add(SingleMethodName);
            Options.Add(SingleMethodIndex);
            Options.Add(SingleMethodGenericArgs);
            Options.Add(Parallelism);
            Options.Add(CustomPESectionAlignment);
            Options.Add(Map);
            Options.Add(MapCsv);
            Options.Add(Pdb);
            Options.Add(PdbPath);
            Options.Add(PerfMap);
            Options.Add(PerfMapPath);
            Options.Add(PerfMapFormatVersion);
            Options.Add(CrossModuleInlining);
            Options.Add(AsyncMethodOptimization);
            Options.Add(NonLocalGenericsModule);
            Options.Add(MethodLayout);
            Options.Add(FileLayout);
            Options.Add(VerifyTypeAndFieldLayout);
            Options.Add(CallChainProfileFile);
            Options.Add(MakeReproPath);
            Options.Add(HotColdSplitting);
            Options.Add(SynthesizeRandomMibc);
            Options.Add(DeterminismStress);

            this.SetAction(result =>
            {
                Result = result;
                CompositeOrInputBubble = result.GetValue(Composite) | result.GetValue(InputBubble);
                if (result.GetValue(OptimizeSpace))
                {
                    OptimizationMode = OptimizationMode.PreferSize;
                }
                else if (result.GetValue(OptimizeTime))
                {
                    OptimizationMode = OptimizationMode.PreferSpeed;
                }
                else if (result.GetValue(Optimize))
                {
                    OptimizationMode = OptimizationMode.Blended;
                }
                else
                {
                    OptimizationMode = OptimizationMode.None;
                }

                try
                {
                    int alignment = result.GetValue(CustomPESectionAlignment);
                    if (alignment != 0)
                    {
                        // Must be a power of two and >= 4096
                        if (alignment < 4096 || (alignment & (alignment - 1)) != 0)
                            throw new CommandLineException(SR.InvalidCustomPESectionAlignment);
                    }

                    string makeReproPath = result.GetValue(MakeReproPath);
                    if (makeReproPath != null)
                    {
                        // Create a repro package in the specified path
                        // This package will have the set of input files needed for compilation
                        // + the original command line arguments
                        // + a rsp file that should work to directly run out of the zip file

                        Helpers.MakeReproPackage(makeReproPath, result.GetValue(OutputFilePath), args,
                            result, new[] { "-r", "--reference", "-u", "--unrooted-input-file-paths", "-m", "--mibc", "--inputbubbleref" });
                    }

                    return new Program(this).Run();
                }
#if DEBUG
                catch (CodeGenerationFailedException ex) when (DumpReproArguments(ex))
                {
                    throw new NotSupportedException(); // Unreachable
                }
#else
                catch (Exception e)
                {
                    Console.ResetColor();
                    Console.ForegroundColor = ConsoleColor.Red;

                    Console.Error.WriteLine("Error: " + e.Message);
                    Console.Error.WriteLine(e.ToString());

                    Console.ResetColor();
                }

                return 1;
#endif
            });
        }

        public static void PrintExtendedHelp(ParseResult _)
        {
            Console.WriteLine(SR.OptionPassingHelp);
            Console.WriteLine();
            Console.WriteLine(SR.DashDashHelp);
            Console.WriteLine();

            string[] ValidArchitectures = new string[] {"arm", "armel", "arm64", "x86", "x64", "riscv64", "loongarch64"};
            string[] ValidOS = new string[] {"windows", "linux", "osx", "ios", "iossimulator", "maccatalyst"};

            Console.WriteLine(String.Format(SR.SwitchWithDefaultHelp, "--targetos", String.Join("', '", ValidOS), Helpers.GetTargetOS(null).ToString().ToLowerInvariant()));
            Console.WriteLine();
            Console.WriteLine(String.Format(SR.SwitchWithDefaultHelp, "--targetarch", String.Join("', '", ValidArchitectures), Helpers.GetTargetArchitecture(null).ToString().ToLowerInvariant()));
            Console.WriteLine();
            Console.WriteLine(String.Format(SR.SwitchWithDefaultHelp, "--type-validation", String.Join("', '", Enum.GetNames<TypeValidationRule>()), nameof(TypeValidationRule.Automatic)));
            Console.WriteLine();

            Console.WriteLine(SR.CrossModuleInliningExtraHelp);
            Console.WriteLine();
            Console.WriteLine(String.Format(SR.LayoutOptionExtraHelp, "--method-layout", String.Join("', '", Enum.GetNames<MethodLayoutAlgorithm>())));
            Console.WriteLine();
            Console.WriteLine(String.Format(SR.LayoutOptionExtraHelp, "--file-layout", String.Join("', '", Enum.GetNames<FileLayoutAlgorithm>())));
            Console.WriteLine();

            Console.WriteLine(SR.InstructionSetHelp);
            foreach (string arch in ValidArchitectures)
            {
                TargetArchitecture targetArch = Helpers.GetTargetArchitecture(arch);
                bool first = true;
                foreach (var instructionSet in Internal.JitInterface.InstructionSetFlags.ArchitectureToValidInstructionSets(targetArch))
                {
                    // Only instruction sets with are specifiable should be printed to the help text
                    if (instructionSet.Specifiable)
                    {
                        if (first)
                        {
                            Console.Write(arch);
                            Console.Write(": ");
                            first = false;
                        }
                        else
                        {
                            Console.Write(", ");
                        }
                        Console.Write(instructionSet.Name);
                    }
                }

                if (first) continue; // no instruction-set found for this architecture

                Console.WriteLine();
            }

            Console.WriteLine();
            Console.WriteLine(SR.CpuFamilies);
            Console.WriteLine(string.Join(", ", Internal.JitInterface.InstructionSetFlags.AllCpuNames));
        }

        private static TargetArchitecture MakeTargetArchitecture(ArgumentResult result)
        {
            string firstToken = result.Tokens.Count > 0 ? result.Tokens[0].Value : null;
            if (firstToken != null && firstToken.Equals("armel", StringComparison.OrdinalIgnoreCase))
            {
                IsArmel = true;
                return Internal.TypeSystem.TargetArchitecture.ARM;
            }

            return Helpers.GetTargetArchitecture(firstToken);
        }

        private static int MakeParallelism(ArgumentResult result)
        {
            if (result.Tokens.Count > 0)
                return int.Parse(result.Tokens[0].Value);

            // Limit parallelism to 24 wide at most by default, more parallelism is unlikely to improve compilation speed
            // as many portions of the process are single threaded, and is known to use excessive memory.
            var parallelism = Math.Min(24, Environment.ProcessorCount);

            // On 32bit platforms restrict it more, as virtual address space is quite limited
            if (!Environment.Is64BitProcess)
                parallelism = Math.Min(4, parallelism);

            return parallelism;
        }

        private static MethodLayoutAlgorithm MakeMethodLayoutAlgorithm(ArgumentResult result)
        {
            if (result.Tokens.Count == 0 )
                return MethodLayoutAlgorithm.DefaultSort;

            return result.Tokens[0].Value.ToLowerInvariant() switch
            {
                "defaultsort" => MethodLayoutAlgorithm.DefaultSort,
                "exclusiveweight" => MethodLayoutAlgorithm.ExclusiveWeight,
                "hotcold" => MethodLayoutAlgorithm.HotCold,
                "instrumentedhotcold" => MethodLayoutAlgorithm.InstrumentedHotCold,
                "hotwarmcold" => MethodLayoutAlgorithm.HotWarmCold,
                "callfrequency" => MethodLayoutAlgorithm.CallFrequency,
                "pettishansen" => MethodLayoutAlgorithm.PettisHansen,
                "random" => MethodLayoutAlgorithm.Random,
                _ => throw new CommandLineException(SR.InvalidMethodLayout)
            };
        }

        private static FileLayoutAlgorithm MakeFileLayoutAlgorithm(ArgumentResult result)
        {
            if (result.Tokens.Count == 0 )
                return FileLayoutAlgorithm.DefaultSort;

            return result.Tokens[0].Value.ToLowerInvariant() switch
            {
                "defaultsort" => FileLayoutAlgorithm.DefaultSort,
                "methodorder" => FileLayoutAlgorithm.MethodOrder,
                _ => throw new CommandLineException(SR.InvalidFileLayout)
            };
        }

#if DEBUG
        private static bool DumpReproArguments(CodeGenerationFailedException ex)
        {
            Console.WriteLine(SR.DumpReproInstructions);

            MethodDesc failingMethod = ex.Method;
            Console.WriteLine(Program.CreateReproArgumentString(failingMethod));
            return false;
        }
#endif
    }
}
