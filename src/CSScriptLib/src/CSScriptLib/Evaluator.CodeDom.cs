#region License...

//-----------------------------------------------------------------------------
// Date:	20/12/15	Time: 9:00
// Module:	CSScriptLib.Eval.Roslyn.cs
//
// This module contains the definition of the Roslyn Evaluator class. Which wraps the common functionality
// of the Mono.CScript.Evaluator class (compiler as service)
//
// Written by Oleg Shilo (oshilo@gmail.com)
//----------------------------------------------
// The MIT License (MIT)
// Copyright (c) 2016 Oleg Shilo
//
// Permission is hereby granted, free of charge, to any person obtaining a copy of this software
// and associated documentation files (the "Software"), to deal in the Software without restriction,
// including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense,
// and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so,
// subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all copies or substantial
// portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT
// LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.
// IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
// WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE
// SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//----------------------------------------------

#endregion License...

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using csscript;
using CSScripting;
using CSScripting.CodeDom;
using Scripting;

namespace CSScriptLib
{
    /// <summary>
    /// Class implementing CodeDom favor of (csc.exe/csc.dll) <see cref="IEvaluator"/>
    /// </summary>
    /// <seealso cref="CSScriptLib.IEvaluator" />
    public class CodeDomEvaluator : EvaluatorBase<CodeDomEvaluator>, IEvaluator
    {
        /// <summary>
        /// The flag indicating if the compilation should happen on the build server or locally.
        /// </summary>
        public static bool CompileOnServer = true;

        /// <summary>
        /// Validates the specified information.
        /// </summary>
        /// <param name="info">The information.</param>
        /// <exception cref="CSScriptException">CompileInfo.RootClass property should only be used with Roslyn evaluator as " +
        ///                     "it addresses the limitation associated with Roslyn. Specifically wrapping ALL scripts in the illegally " +
        ///                     "named parent class. You are using CodeDomEvaluator so you should not set CompileInfo.RootClass to any custom value</exception>
        protected override void Validate(CompileInfo info)
        {
            if (info != null && info.RootClass != Globals.RootClassName)
                throw new CSScriptException("CompileInfo.RootClass property should only be used with Roslyn evaluator as " +
                    "it addresses the limitation associated with Roslyn. Specifically wrapping ALL scripts in the illegally " +
                    "named parent class. You are using CodeDomEvaluator so you should not set CompileInfo.RootClass to any custom value");
        }

        /// <summary>
        /// Compiles the specified script text.
        /// </summary>
        /// <param name="scriptText">The script text.</param>
        /// <param name="scriptFile">The script file.</param>
        /// <param name="info">The information.</param>
        /// <returns>The method result.</returns>
        override protected (byte[] asm, byte[] pdb) Compile(string scriptText, string scriptFile, CompileInfo info)
        {
            // Debug.Assert(false);
            string tempScriptFile = null;
            string injection_file = null;
            try
            {
                if (scriptFile == null)
                {
                    tempScriptFile = CSScript.GetScriptTempFile();
                    File.WriteAllText(tempScriptFile, scriptText);
                }

                var project = Project.GenerateProjectFor(tempScriptFile ?? scriptFile);
                var refs = project.Refs.Concat(this.GetReferencedAssembliesFiles()).Distinct().ToArray();
                var sources = project.Files;

                if (info?.AssemblyFile != null)
                {
                    injection_file = CoreExtensions.GetScriptedCodeAttributeInjectionCode(info.AssemblyFile);
                    sources = sources.Concat(new[] { injection_file }).ToArray();
                }

                (byte[], byte[]) result = CompileAssemblyFromFileBatch_with_Csc(sources, refs, info?.AssemblyFile, this.IsDebug, info);

                return result;
            }
            finally
            {
                if (this.IsDebug)
                    CSScript.NoteTempFile(tempScriptFile);
                else
                    tempScriptFile.FileDelete(rethrow: false);

                injection_file.FileDelete(rethrow: false);

                CSScript.StartPurgingOldTempFiles(ignoreCurrentProcessScripts: true);
            }
        }

        static string dotnet = "dotnet";

        (byte[] asm, byte[] pdb) CompileAssemblyFromFileBatch_with_Csc(string[] fileNames,
                                                                       string[] ReferencedAssemblies,
                                                                       string outAssembly,
                                                                       bool IncludeDebugInformation,
                                                                       CompileInfo info)
        {
            string projectName = fileNames.First().GetFileName();

            var engine_dir = this.GetType().Assembly.Location.GetDirName();
            var cache_dir = CSExecutor.ScriptCacheDir; // C:\Users\user\AppData\Local\Temp\csscript.core\cache\1822444284
            var build_dir = cache_dir.PathJoin(".build", projectName);

            try
            {
                build_dir.DeleteDir(handleExceptions: true)
                         .EnsureDir();

                var sources = new List<string>(fileNames); // sources may need to hold more than fileNames

                var ref_assemblies = ReferencedAssemblies.Where(x => !x.IsSharedAssembly())
                                                         .Where(Path.IsPathRooted)
                                                         .ToList();

                var refs = new StringBuilder();
                var assembly = build_dir.PathJoin(projectName + ".dll");

                var result = new CompilerResults();

                //pseudo-gac as .NET core does not support GAC but rather common assemblies.
                var gac = typeof(string).Assembly.Location.GetDirName();

                var refs_args = new List<string>();
                var source_args = new List<string>();
                var common_args = new List<string>();

                common_args.Add("/utf8output");
                common_args.Add("/nostdlib+");
                common_args.Add("-t:library");

                if (info?.CompilerOptions.HasText() == true)
                    common_args.Add(info.CompilerOptions);

                // common_args.Add("/t:exe"); // need always build exe so "top-class" feature is supported even when building dlls

                if (IncludeDebugInformation)
                    if (Runtime.IsCore)
                        common_args.Add("/debug:portable");  // on .net full it is "/debug+"
                    else
                        common_args.Add("/debug:full");  // on .net full it is "/debug+"

                common_args.Add("-define:TRACE;NETCORE;CS_SCRIPT");

                if (Runtime.IsCore)
                {
                    var gac_asms = Directory.GetFiles(gac, "System.*.dll").ToList();
                    gac_asms.AddRange(Directory.GetFiles(gac, "netstandard.dll"));
                    // Microsoft.DiaSymReader.Native.amd64.dll is a native dll
                    gac_asms.AddRange(Directory.GetFiles(gac, "Microsoft.*.dll").Where(x => !x.Contains("Native")));

                    foreach (string file in gac_asms.Concat(ref_assemblies).Distinct())
                        refs_args.Add($"/r:\"{file}\"");
                }
                else
                {
                    // foreach (string file in ref_assemblies)
                    // refs_args.Add($"/r:\"{file}\"");
                    refs_args.Add($"/r:\"System.Design.dll\"");
                    refs_args.Add($"/r:\"mscorlib.dll\"");
                }

                foreach (string file in sources)
                    source_args.Add($"\"{file}\"");

                string cmd;

                if (Runtime.IsCore)
                {
                    if (CompileOnServer && Globals.BuildServerIsDeployed)
                    {
                        var usingCli = false;
                        /////////////////////
                        if (usingCli)
                        {
                            // using CLI app to send/receive sockets data
                            dotnet.RunAsync($"\"{Globals.build_server}\" -start -port:{17001}");
                            cmd = $@"""{Globals.build_server}"" -port:{17001} csc {common_args.JoinBy(" ")}  /out:""{assembly}"" {refs_args.JoinBy(" ")} {source_args.JoinBy(" ")}";
                            result.NativeCompilerReturnValue = dotnet.Run(cmd, build_dir, x => result.Output.Add(x));
                        }
                        else
                        {
                            // using sockets directly
                            var request = $@"csc {common_args.JoinBy(" ")}  /out:""{assembly}"" {refs_args.JoinBy(" ")} {source_args.JoinBy(" ")}"
                                          .SplitCommandLine();

                            // ensure server running
                            // it will gracefully exit if another instance is running
                            dotnet.RunAsync($@"""{Globals.build_server}"" -listen -port:{17001}");
                            Thread.Sleep(30);

                            // var response = BuildServer.SendBuildRequest(request, BuildServer.serverPort);
                            var response = BuildServer.SendBuildRequest(request, 17001);

                            result.NativeCompilerReturnValue = 0;
                            result.Output.AddRange(response.GetLines());
                        }
                    }
                    else
                    {
                        cmd = $@"""{Globals.csc}"" {common_args.JoinBy(" ")} /out:""{assembly}"" {refs_args.JoinBy(" ")} {source_args.JoinBy(" ")}";

                        result.NativeCompilerReturnValue = dotnet.Run(cmd, build_dir, x => result.Output.Add(x));
                    }
                }
                else
                {
                    cmd = $@" {common_args.JoinBy(" ")}  /out:""{assembly}"" {refs_args.JoinBy(" ")} {source_args.JoinBy(" ")}";

                    result.NativeCompilerReturnValue = Globals.csc.Run(cmd, build_dir, x => result.Output.Add(x));
                }

                result.ProcessErrors();

                result.Errors
                      .ForEach(x =>
                      {
                          // by default x.FileName is a file name only
                          x.FileName = fileNames.FirstOrDefault(f => f.EndsWith(x.FileName ?? "")) ?? x.FileName;
                      });

                if (result.NativeCompilerReturnValue == 0 && File.Exists(assembly))
                {
                    if (outAssembly != null)
                        File.Copy(assembly, outAssembly, true);

                    if (!IncludeDebugInformation)
                    {
                        if (outAssembly != null)
                            File.Copy(assembly, outAssembly, true);

                        return (File.ReadAllBytes(assembly), null);
                    }
                    else
                    {
                        if (outAssembly != null)
                        {
                            File.Copy(assembly, outAssembly, true);
                            File.Copy(assembly.ChangeExtension(".pdb"), outAssembly.ChangeExtension(".pdb"), true);
                        }

                        return (File.ReadAllBytes(assembly),
                                File.ReadAllBytes(assembly.ChangeExtension(".pdb")));
                    }
                }
                else
                {
                    if (result.Errors.IsEmpty())
                    {
                        // unknown error; e.g. invalid compiler params
                        result.Errors.Add(new CompilerError { ErrorText = "Unknown compiler error" });
                    }
                    throw CompilerException.Create(result.Errors, true, true);
                }
            }
            finally
            {
                build_dir.DeleteDir(handleExceptions: true);
            }
        }

        List<string> referencedAssemblies = new List<string>();

        /// <summary>
        /// References the given assembly.
        /// <para>It is safe to call this method multiple times
        /// for the same assembly. If the assembly already referenced it will not
        /// be referenced again.
        /// </para>
        /// </summary>
        /// <param name="assembly">The assembly instance.</param>
        /// <returns>
        /// The instance of the <see cref="T:CSScriptLib.IEvaluator" /> to allow  fluent interface.
        /// </returns>
        /// <exception cref="System.Exception">Current version of {EngineName} doesn't support referencing assemblies " +
        ///                          "which are not loaded from the file location.</exception>
        public override IEvaluator ReferenceAssembly(Assembly assembly)
        {
            if (assembly != null)//this check is needed when trying to load partial name assemblies that result in null
            {
                if (assembly.Location.IsEmpty())
                    throw new Exception(
                        $"Current version of CodeDom evaluator (csc.exe) doesn't support referencing assemblies " +
                         "which are not loaded from the file location.");

                var asmFile = assembly.Location;

                if (referencedAssemblies.FirstOrDefault(x => asmFile.SamePathAs(x)) == null)
                    referencedAssemblies.Add(asmFile);
            }
            return this;
        }

        /// <summary>
        /// Gets the referenced assemblies files.
        /// </summary>
        /// <returns>The method result.</returns>
        public override string[] GetReferencedAssembliesFiles()
            => referencedAssemblies.ToArray();

        /// <summary>
        /// Loads and returns set of referenced assemblies.
        /// <para>
        /// Notre: the set of assemblies is cleared on Reset.
        /// </para>
        /// </summary>
        /// <returns>The method result.</returns>
        public override Assembly[] GetReferencedAssemblies()
            => referencedAssemblies.Select(Assembly.LoadFile).ToArray();
    }
}